using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     When permissions are trusted from the token, two requests carrying different claims for the
///     same person are two different answers.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Keying the cross-request cache by tenant and user id alone is right while the answer
///         is resolved from a <b>store</b> — the same person in the same tenant gets the same
///         permissions. It is wrong the moment <c>TrustPermissionClaims</c> is on, because then the
///         answer is a function of the <b>token</b>: the first request would decide what that user id
///         can do, and every later one with a different token would be served the first answer.
///     </para>
///     <para>
///         The Showcase's generated auth contracts call every endpoint as <c>contract-{permission}</c>
///         carrying that one permission. With the narrow key, a later request as the same id with two
///         permissions is refused — <c>403</c> on a create whose header says it is allowed — and, read
///         the other way round, a request carrying a permission that grants nothing is admitted,
///         because that id has already been resolved.
///     </para>
///     <para>
///         The answer is not to stop caching: it is to key the entry by what the answer depends on. With
///         the claims trusted, that includes the claims.
///     </para>
/// </remarks>
public class PermissionCacheKeyClaimsTests
{
    /// <summary>A provider that answers with whatever the principal's permission claims say.</summary>
    private sealed class ClaimsProvider : IPermissionProvider
    {
        public int Order => 0;

        public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
            ICurrentUser user, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(
                    user.Claims.TryGetValue("permission", out var claims) ? claims : [],
                    StringComparer.OrdinalIgnoreCase));
    }

    private sealed class ClaimedUser(string id, params string[] permissions) : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>
            {
                ["permission"] = permissions
            };

        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
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

    private static IOptions<AuthorizationOptions> Trusting()
        => Options.Create(new AuthorizationOptions
        {
            TrustPermissionClaims = true,
            CacheOptions = new PermissionCacheOptions()
        });

    private static IOptions<AuthorizationOptions> Resolving()
        => Options.Create(new AuthorizationOptions
        {
            TrustPermissionClaims = false,
            CacheOptions = new PermissionCacheOptions()
        });

    /// <summary>The second request keeps its own permissions.</summary>
    [Fact]
    public void TwoTokensForOnePerson_DoNotShareACacheEntry()
    {
        var cache = new RecordingCache();

        var first = new CachedPermissionResolver(
            [new ClaimsProvider()], new ClaimedUser("u", "booking.reservation.update"), Trusting(), cache);

        var second = new CachedPermissionResolver(
            [new ClaimsProvider()], new ClaimedUser("u", "booking.reservation.create", "booking.reservation.update"),
            Trusting(), cache);

        first.Permissions.Should().BeEquivalentTo(["booking.reservation.update"]);
        second.Permissions.Should().BeEquivalentTo(
            ["booking.reservation.create", "booking.reservation.update"],
            "the answer is a function of the token, and this token says something else");
    }

    /// <summary>
    ///     The control: the same claims resolve once, so the cache is still a cache.
    /// </summary>
    /// <remarks>
    ///     Without it, "the second request sees its own permissions" is satisfied by keying every
    ///     entry uniquely — a cache that never hits, which is the cost the entry exists to avoid.
    /// </remarks>
    [Fact]
    public void TwoRequestsWithTheSameClaims_ShareTheEntry()
    {
        var cache = new RecordingCache();

        var first = new CachedPermissionResolver(
            [new ClaimsProvider()], new ClaimedUser("u", "a.b.c"), Trusting(), cache);
        var second = new CachedPermissionResolver(
            [new ClaimsProvider()], new ClaimedUser("u", "a.b.c"), Trusting(), cache);

        _ = first.Permissions;
        _ = second.Permissions;

        cache.Keys.Count.Should().Be(1, "the same person with the same token is one answer");
    }

    /// <summary>
    ///     The other control: with the claims <b>not</b> trusted, the key does not grow a claim part.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the posture the framework recommends — permissions resolved server-side from
    ///     roles and groups — and there the answer depends on the person, not on the token. Keying by
    ///     the claims there would partition the cache by something the answer does not read, and a
    ///     store lookup would run for every distinct token of the same person.
    /// </remarks>
    [Fact]
    public void WithTheClaimsNotTrusted_TheKeyIsTheUserAlone()
    {
        var cache = new RecordingCache();

        var first = new CachedPermissionResolver(
            [new ClaimsProvider()], new ClaimedUser("u", "one"), Resolving(), cache);
        var second = new CachedPermissionResolver(
            [new ClaimsProvider()], new ClaimedUser("u", "two"), Resolving(), cache);

        _ = first.Permissions;
        _ = second.Permissions;

        cache.Keys.Count.Should().Be(1,
            "server-side resolution answers for the person, whatever token carried the request");
    }
}
