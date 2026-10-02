using System.Collections.Concurrent;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Caching;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Unit;

public class CachedPermissionResolverCacheTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class FakeProvider(int order, params string[] permissions) : IPermissionProvider
    {
        public int CallCount { get; private set; }
        public int Order => order;

        public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
            ICurrentUser user, CancellationToken ct = default)
        {
            CallCount++;
            return ValueTask.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class TestUser(
        Dictionary<string, IReadOnlyList<string>> claims,
        bool isAuthenticated = true,
        string id = "user-1",
        string? tenantId = null) : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => "Test";
        public bool IsAuthenticated => isAuthenticated;
        public PrincipalKind Kind => isAuthenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
        public string? TenantId => tenantId;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => claims;
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private static IOptions<AuthorizationOptions> CreateOptions(PermissionCacheOptions? cacheOptions = null)
    {
        var options = new AuthorizationOptions { CacheOptions = cacheOptions };
        return Options.Create(options);
    }

    // =========================================================================
    // Tests — no cache (L1 behavior preserved)
    // =========================================================================

    [Fact]
    public void NoCacheAndNoOptions_FallsBackToRequestScoped()
    {
        var provider = new FakeProvider(0, "orders.create");
        var resolver = new CachedPermissionResolver(
            [provider],
            new TestUser(new Dictionary<string, IReadOnlyList<string>>()));

        resolver.HasPermission("orders.create").Should().BeTrue();
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public void NoCacheWithOptions_FallsBackToRequestScoped()
    {
        var provider = new FakeProvider(0, "orders.create");
        var resolver = new CachedPermissionResolver(
            [provider],
            new TestUser(new Dictionary<string, IReadOnlyList<string>>()),
            CreateOptions(new PermissionCacheOptions()),
            cache: null);

        resolver.HasPermission("orders.create").Should().BeTrue();
        provider.CallCount.Should().Be(1);
    }

    // =========================================================================
    // Tests — IsInGroup / HasScope
    // =========================================================================

    [Fact]
    public void IsInGroup_MatchingGroup_ReturnsTrue()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["group"] = ["engineering"]
        };
        var resolver = new CachedPermissionResolver(
            [], new TestUser(claims));

        resolver.IsInGroup("engineering").Should().BeTrue();
    }

    [Fact]
    public void IsInGroup_NoGroups_ReturnsFalse()
    {
        var resolver = new CachedPermissionResolver(
            [], new TestUser(new Dictionary<string, IReadOnlyList<string>>()));

        resolver.IsInGroup("engineering").Should().BeFalse();
    }

    [Fact]
    public void HasScope_MatchingScope_ReturnsTrue()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["scope"] = ["read", "write"]
        };
        var resolver = new CachedPermissionResolver(
            [], new TestUser(claims));

        resolver.HasScope("read").Should().BeTrue();
        resolver.HasScope("write").Should().BeTrue();
    }

    [Fact]
    public void HasScope_NoScopes_ReturnsFalse()
    {
        var resolver = new CachedPermissionResolver(
            [], new TestUser(new Dictionary<string, IReadOnlyList<string>>()));

        resolver.HasScope("read").Should().BeFalse();
    }

    // =========================================================================
    // Tests — unauthenticated user skips cache
    // =========================================================================

    [Fact]
    public void UnauthenticatedUser_SkipsCache_FallsBackToRequestScoped()
    {
        var provider = new FakeProvider(0, "public.read");
        var resolver = new CachedPermissionResolver(
            [provider],
            new TestUser(new Dictionary<string, IReadOnlyList<string>>(), isAuthenticated: false),
            CreateOptions(new PermissionCacheOptions()));

        resolver.HasPermission("public.read").Should().BeTrue();
        provider.CallCount.Should().Be(1);
    }

    // =========================================================================
    // Tests — multi-tenant cache isolation (Fase 0.1 regression guard)
    // =========================================================================

    [Fact]
    public void TenantUser_DoesNotCollideWith_GlobalUser_WithSameId()
    {
        // Regression: two users with the same Id, one scoped to a tenant and
        // one without, must not share cache keys — otherwise the cache can
        // leak permissions from one scope into the other.
        var tenantProvider = new FakeProvider(0, "tenant.admin");
        var globalProvider = new FakeProvider(0, "public.read");
        var cache = new RecordingCache();

        var tenantUser = new TestUser(new Dictionary<string, IReadOnlyList<string>>(), id: "shared", tenantId: "acme");
        var globalUser = new TestUser(new Dictionary<string, IReadOnlyList<string>>(), id: "shared");

        var tenantResolver = new CachedPermissionResolver(
            [tenantProvider], tenantUser, CreateOptions(new PermissionCacheOptions()), cache);
        var globalResolver = new CachedPermissionResolver(
            [globalProvider], globalUser, CreateOptions(new PermissionCacheOptions()), cache);

        tenantResolver.HasPermission("tenant.admin").Should().BeTrue();
        // Fresh resolver — no request-scoped cache to mask collision.
        globalResolver.HasPermission("tenant.admin").Should().BeFalse();
        globalResolver.HasPermission("public.read").Should().BeTrue();

        cache.Keys.Should().HaveCount(2, "tenant and global scope must produce distinct keys");
        cache.Keys.Should().Contain(k => k.Contains(":t:acme:u:shared"));
        cache.Keys.Should().Contain(k => k.Contains(":g:u:shared"));
    }

    [Fact]
    public void TwoTenants_WithSameUserId_GetIsolatedCacheEntries()
    {
        var acmeProvider = new FakeProvider(0, "acme.only");
        var bazProvider = new FakeProvider(0, "baz.only");
        var cache = new RecordingCache();

        var acmeUser = new TestUser(new Dictionary<string, IReadOnlyList<string>>(), id: "u", tenantId: "acme");
        var bazUser = new TestUser(new Dictionary<string, IReadOnlyList<string>>(), id: "u", tenantId: "baz");

        var acmeResolver = new CachedPermissionResolver(
            [acmeProvider], acmeUser, CreateOptions(new PermissionCacheOptions()), cache);
        var bazResolver = new CachedPermissionResolver(
            [bazProvider], bazUser, CreateOptions(new PermissionCacheOptions()), cache);

        acmeResolver.HasPermission("acme.only").Should().BeTrue();
        bazResolver.HasPermission("acme.only").Should().BeFalse("baz tenant must not see acme cache");
        bazResolver.HasPermission("baz.only").Should().BeTrue();

        cache.Keys.Should().HaveCount(2);
    }

    // Minimal ICacheStack double — stores everything in a dictionary and
    // resolves factories synchronously. Enough to exercise key partitioning.
    private sealed class RecordingCache : ICacheStack
    {
        private readonly ConcurrentDictionary<string, object?> _store = new();
        public IReadOnlyCollection<string> Keys => _store.Keys.ToList();

        public async ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var existing))
                return (T)existing!;
            var value = await factory(ct).ConfigureAwait(false);
            _store[key] = value;
            return value;
        }

        public async ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var existing))
                return (T)existing!;
            var result = await factory(ct).ConfigureAwait(false);
            if (result.ShouldCache)
                _store[key] = result.Value;
            return result.Value;
        }

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => _store.TryGetValue(key, out var v) ? new ValueTask<T?>((T?)v) : new ValueTask<T?>(default(T));

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
            => _store.TryGetValue(key, out var v) ? new ValueTask<(bool, T?)>((true, (T?)v)) : new ValueTask<(bool, T?)>((false, default));

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            _store[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) => ValueTask.CompletedTask;
    }
}
