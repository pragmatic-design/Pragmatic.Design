using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     The permission cache has to be partitioned by the tenant the request is actually being served
///     for, not by a claim that may never have been issued.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ There are two notions of tenant and they do not talk to each other:
///         <see cref="ITenantContext" />.<c>TenantId</c> is what the tenant middleware resolved (an
///         <c>X-Tenant-Id</c> header, a route segment, a subdomain), while
///         <see cref="ICurrentUser" />.<c>TenantId</c> is the <c>tenant_id</c> claim on the
///         principal. An application that resolves tenants by header and issues no such claim — a
///         normal, working configuration, measured in a consumer app — has a null
///         <c>ICurrentUser.TenantId</c> on every request.
///     </para>
///     <para>
///         <c>TwoTenants_WithSameUserId_GetIsolatedCacheEntries</c> covers the case where the claim
///         <em>is</em> present. This file covers the case where it is not, which is the one that
///         leaks: the key collapses to the global shape and one person carries the first workspace's
///         permissions into the second.
///     </para>
/// </remarks>
public class PermissionCacheKeyTenantTests
{
    private sealed class FakeProvider(params string[] permissions) : IPermissionProvider
    {
        public int Order => 0;

        public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
            ICurrentUser user, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>A principal with no tenant claim — what a header-resolved deployment actually has.</summary>
    private sealed class ClaimlessUser(string id) : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();

        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private sealed class FixedTenant(string? tenantId) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantId;
        public bool IsResolved => tenantId is { Length: > 0 };
        public IReadOnlyDictionary<string, string?> Properties
            => new Dictionary<string, string?> { ["tenant"] = tenantId };
    }

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
            => _store.TryGetValue(key, out var v)
                ? new ValueTask<(bool, T?)>((true, (T?)v))
                : new ValueTask<(bool, T?)>((false, default));

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

        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }

    private static IOptions<AuthorizationOptions> WithCache()
        => Options.Create(new AuthorizationOptions { CacheOptions = new PermissionCacheOptions() });

    [Fact]
    public void TwoTenants_ResolvedByHeaderWithNoTenantClaim_DoNotShareACacheEntry()
    {
        var cache = new RecordingCache();

        // The same person, in two workspaces, with no tenant claim anywhere — and the two workspaces
        // grant different things, which is the whole point of a per-tenant permission store.
        var acme = new CachedPermissionResolver(
            [new FakeProvider("acme.only")], new ClaimlessUser("u"), WithCache(), cache,
            new FixedTenant("acme"));

        var globex = new CachedPermissionResolver(
            [new FakeProvider("globex.only")], new ClaimlessUser("u"), WithCache(), cache,
            new FixedTenant("globex"));

        acme.HasPermission("acme.only").Should().BeTrue();

        globex.HasPermission("acme.only").Should().BeFalse(
            "Globex must not inherit what Acme granted, and the cache key is the only thing keeping "
            + "them apart when neither principal carries a tenant claim");

        globex.HasPermission("globex.only").Should().BeTrue();

        cache.Keys.Should().HaveCount(2, "one entry per tenant, not one entry for the person");
    }

    /// <summary>The claim still partitions when it is present, and it wins over an absent context.</summary>
    /// <remarks>
    ///     The control beside the test above: without it, making the resolver ignore
    ///     <c>ICurrentUser.TenantId</c> entirely would also turn that test green.
    /// </remarks>
    [Fact]
    public void WithNoTenantContext_TheTenantClaimStillPartitions()
    {
        var cache = new RecordingCache();

        var acme = new CachedPermissionResolver(
            [new FakeProvider("acme.only")], new ClaimedUser("u", "acme"), WithCache(), cache);

        var globex = new CachedPermissionResolver(
            [new FakeProvider("globex.only")], new ClaimedUser("u", "globex"), WithCache(), cache);

        acme.HasPermission("acme.only").Should().BeTrue();
        globex.HasPermission("acme.only").Should().BeFalse();

        cache.Keys.Should().HaveCount(2);
    }

    private sealed class ClaimedUser(string id, string tenant) : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => tenant;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();

        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }
}
