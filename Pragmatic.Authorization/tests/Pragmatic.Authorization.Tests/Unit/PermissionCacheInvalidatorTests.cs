using System.Collections.Concurrent;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Providers;
using Pragmatic.Authorization.Stores;
using Pragmatic.Caching;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Unit;

public class PermissionCacheInvalidatorTests
{
    [Fact]
    public async Task InvalidateUserAsync_InvalidatesUserTag()
    {
        var cache = new RecordingCache();
        var invalidator = CreateInvalidator(cache);

        await invalidator.InvalidateUserAsync("user-42");

        cache.InvalidatedTags.Should().ContainSingle().Which.Should().Be("user:user-42");
    }

    [Fact]
    public async Task InvalidateTenantAsync_InvalidatesTenantTag()
    {
        var cache = new RecordingCache();
        var invalidator = CreateInvalidator(cache);

        await invalidator.InvalidateTenantAsync("acme");

        cache.InvalidatedTags.Should().ContainSingle().Which.Should().Be("tenant:acme");
    }

    [Fact]
    public async Task InvalidateUserAsync_DoesNotTouchTenantTag()
    {
        var cache = new RecordingCache();
        var invalidator = CreateInvalidator(cache);

        await invalidator.InvalidateUserAsync("user-1");

        cache.InvalidatedTags.Should().NotContain(t => t.StartsWith("tenant:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidateUserAsync_ForwardsCancellationToken()
    {
        var cache = new RecordingCache();
        var invalidator = CreateInvalidator(cache);
        using var cts = new CancellationTokenSource();

        await invalidator.InvalidateUserAsync("user-1", cts.Token);

        cache.LastToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task InvalidateTenantAsync_ForwardsCancellationToken()
    {
        var cache = new RecordingCache();
        var invalidator = CreateInvalidator(cache);
        using var cts = new CancellationTokenSource();

        await invalidator.InvalidateTenantAsync("acme", cts.Token);

        cache.LastToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task InvalidateUserAsync_MultipleUsers_InvalidatesEachTag()
    {
        var cache = new RecordingCache();
        var invalidator = CreateInvalidator(cache);

        await invalidator.InvalidateUserAsync("a");
        await invalidator.InvalidateUserAsync("b");

        cache.InvalidatedTags.Should().ContainInOrder("user:a", "user:b");
    }

    // =========================================================================
    // #B-C1 — group-aware role invalidation (transitive role → group reverse lookup)
    // =========================================================================

    [Fact]
    public async Task InvalidateRoleAsync_WithoutGroupStore_InvalidatesOnlyRoleTag()
    {
        var cache = new RecordingCache();
        var invalidator = CreateInvalidator(cache); // no group store

        await invalidator.InvalidateRoleAsync("editor");

        cache.InvalidatedTags.Should().ContainSingle().Which.Should().Be("role:editor");
    }

    [Fact]
    public async Task InvalidateRoleAsync_TransitiveViaGroup_InvalidatesRoleAndGrantingGroupTags()
    {
        // The core bug: a user in group "customer-care" (which grants "editor") is cached tagged
        // group:customer-care, NOT role:editor. Invalidating the role must still reach them.
        var cache = new RecordingCache();
        var groups = new InMemoryGroupRoleStore();
        groups.AddGroup("customer-care", ["editor", "viewer"]);
        groups.AddGroup("read-only", ["viewer"]); // does NOT grant editor
        var invalidator = new PermissionCacheInvalidator(cache, groups);

        await invalidator.InvalidateRoleAsync("editor");

        cache.InvalidatedTags.Should().Contain("role:editor");
        cache.InvalidatedTags.Should().Contain("group:customer-care");
        cache.InvalidatedTags.Should().NotContain("group:read-only");
    }

    [Fact]
    public async Task InvalidateRoleAsync_MultipleGroupsGrantRole_InvalidatesEachGroupTag()
    {
        var cache = new RecordingCache();
        var groups = new InMemoryGroupRoleStore();
        groups.AddGroup("g1", ["editor"]);
        groups.AddGroup("g2", ["editor", "viewer"]);
        groups.AddGroup("g3", ["viewer"]);
        var invalidator = new PermissionCacheInvalidator(cache, groups);

        await invalidator.InvalidateRoleAsync("editor");

        cache.InvalidatedTags.Should().Contain(["role:editor", "group:g1", "group:g2"]);
        cache.InvalidatedTags.Should().NotContain("group:g3");
    }

    [Fact]
    public async Task InvalidateRoleAsync_GroupRoleMatchIsCaseInsensitive()
    {
        var cache = new RecordingCache();
        var groups = new InMemoryGroupRoleStore();
        groups.AddGroup("care", ["Editor"]);
        var invalidator = new PermissionCacheInvalidator(cache, groups);

        await invalidator.InvalidateRoleAsync("editor");

        cache.InvalidatedTags.Should().Contain("group:care");
    }

    [Fact]
    public async Task InvalidateRoleAsync_WithGroupStore_ForwardsCancellationToken()
    {
        var cache = new RecordingCache();
        var groups = new InMemoryGroupRoleStore();
        groups.AddGroup("care", ["editor"]);
        var invalidator = new PermissionCacheInvalidator(cache, groups);
        using var cts = new CancellationTokenSource();

        await invalidator.InvalidateRoleAsync("editor", cts.Token);

        cache.LastToken.Should().Be(cts.Token);
    }

    // End-to-end proof: a role-permission reduction + InvalidateRoleAsync evicts a user who holds
    // the role ONLY transitively through a group. Exercises the real resolver, provider chain,
    // tag-aware cache and invalidator together — the exact scenario the bug left stale.
    [Fact]
    public async Task RoleReduction_ThenInvalidateRole_EvictsTransitiveGroupUser()
    {
        var cache = new TagAwareCache();
        var groups = new InMemoryGroupRoleStore();
        groups.AddGroup("customer-care", ["editor"]);
        var roles = new MutableRoleStore();
        roles.Set("editor", "docs.read", "docs.write");

        var user = new TestUser(new Dictionary<string, IReadOnlyList<string>>
        {
            // NOTE: only a group claim — no direct "role" claim. The entry is tagged group:customer-care.
            ["group"] = ["customer-care"]
        });
        var options = Options.Create(new AuthorizationOptions { CacheOptions = new PermissionCacheOptions() });

        // 1. First resolution caches the permission set (tagged group:customer-care).
        var first = new CachedPermissionResolver(
            [new GroupExpansionProvider(groups, roles)], user, options, cache);
        (await first.HasPermissionAsync("docs.write")).Should().BeTrue();

        // 2. Admin reduces the role's permissions...
        roles.Set("editor", "docs.read"); // docs.write revoked

        // 3. ...and invalidates the ROLE. The user is tagged group:customer-care, so invalidating
        //    role:editor alone would miss them. The invalidator reverse-looks-up the group.
        var invalidator = new PermissionCacheInvalidator(cache, groups);
        await invalidator.InvalidateRoleAsync("editor");

        // 4. A fresh request-scoped resolver re-reads through the (now-evicted) cache entry.
        var second = new CachedPermissionResolver(
            [new GroupExpansionProvider(groups, roles)], user, options, cache);
        (await second.HasPermissionAsync("docs.write")).Should().BeFalse("the reduced role must no longer grant docs.write");
        (await second.HasPermissionAsync("docs.read")).Should().BeTrue();
    }

    // The concrete invalidator is internal; resolve it through its public interface
    // contract by constructing it via the internal type using its public surface.
    private static IPermissionCacheInvalidator CreateInvalidator(ICacheStack cache)
        => new PermissionCacheInvalidator(cache);

    // Mutable role→permission store so a test can reduce a role's grant at runtime.
    private sealed class MutableRoleStore : IRolePermissionStore
    {
        private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _roles =
            new(StringComparer.OrdinalIgnoreCase);

        public void Set(string role, params string[] permissions)
            => _roles[role] = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

        public ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(string roleName, CancellationToken ct = default)
            => ValueTask.FromResult(_roles.TryGetValue(roleName, out var p)
                ? p
                : (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public ValueTask<IReadOnlyList<string>> GetAllRolesAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<string>>(_roles.Keys.ToList());
    }

    private sealed class TestUser(
        Dictionary<string, IReadOnlyList<string>> claims,
        string id = "user-1") : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => claims;
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    // Tag-aware cache: stores each entry with the tags from CacheEntryOptions and evicts by tag,
    // so the resolver's group:{g} tagging and the invalidator's reverse lookup are exercised for real.
    private sealed class TagAwareCache : ICacheStack
    {
        private readonly ConcurrentDictionary<string, (object? Value, string[] Tags)> _store = new();

        public async ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var existing))
                return (T)existing.Value!;
            var value = await factory(ct).ConfigureAwait(false);
            _store[key] = (value, options?.Tags.ToArray() ?? []);
            return value;
        }

        public async ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var existing))
                return (T)existing.Value!;
            var result = await factory(ct).ConfigureAwait(false);
            if (result.ShouldCache)
                _store[key] = (result.Value, options?.Tags.ToArray() ?? []);
            return result.Value;
        }

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => _store.TryGetValue(key, out var v) ? new((T?)v.Value) : new(default(T));

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
            => _store.TryGetValue(key, out var v) ? new((true, (T?)v.Value)) : new((false, default));

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            _store[key] = (value, options?.Tags.ToArray() ?? []);
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
        {
            foreach (var kvp in _store)
                if (Array.IndexOf(kvp.Value.Tags, tag) >= 0)
                    _store.TryRemove(kvp.Key, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        {
            var set = new HashSet<string>(tags, StringComparer.Ordinal);
            foreach (var kvp in _store)
                if (kvp.Value.Tags.Any(set.Contains))
                    _store.TryRemove(kvp.Key, out _);
            return ValueTask.CompletedTask;
        }
    }

    // Minimal ICacheStack double — records the tag invalidation calls the
    // invalidator is contractually expected to make.
    private sealed class RecordingCache : ICacheStack
    {
        private readonly List<string> _invalidatedTags = [];

        public IReadOnlyList<string> InvalidatedTags => _invalidatedTags;
        public CancellationToken LastToken { get; private set; }

        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
        {
            _invalidatedTags.Add(tag);
            LastToken = ct;
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        {
            _invalidatedTags.AddRange(tags);
            LastToken = ct;
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => factory(ct);

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => Unwrap(factory, ct);

        private static async ValueTask<T> Unwrap<T>(Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory, CancellationToken ct)
            => (await factory(ct).ConfigureAwait(false)).Value;

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => new(default(T));

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
            => new((false, default));

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
