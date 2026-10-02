using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Cache;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Delegation;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Where a <c>[Cacheable]</c> action's answer is kept for a delegated caller. The authority of a
///     delegated session is composed from the subject, the actor, the policy and — under
///     <see cref="DelegationPolicy.GrantScoped" /> — the grant, so an answer computed under one of them
///     is not an answer for another.
/// </summary>
public class ACachedAnswerUnderDelegationTests
{
    [Fact]
    public void TwoGrants_OfOneActor_ForOneSubject_AreKeptApart()
    {
        var underTheBroadGrant = KeyFor(new Delegated("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad"));
        var underTheNarrowGrant = KeyFor(new Delegated("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-narrow"));

        underTheBroadGrant.Should().NotBe(underTheNarrowGrant,
            "two grants can carry different permissions for the same subject and actor");
    }

    /// <summary>The control: the same grant is the same authority, and shares its entry.</summary>
    [Fact]
    public void TheSameGrant_SharesTheEntry()
    {
        var first = KeyFor(new Delegated("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad"));
        var second = KeyFor(new Delegated("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad"));

        first.Should().Be(second);
    }

    /// <summary>
    ///     A refused delegation authorises nothing, and it is refused without its identifiers changing:
    ///     the same session crosses its expiry, or its actor turns out to belong to another tenant. What
    ///     an admitted delegation was answered must not be handed to it. Cross-tenant stands in for
    ///     expiry here because it refuses with the delegation context identical on both sides, which a
    ///     clock-bound expiry cannot do in a unit test without changing <c>ExpiresAt</c> as well.
    /// </summary>
    [Fact]
    public void ARefusedDelegation_DoesNotShareTheAdmittedOnesEntry()
    {
        var delegation = new Delegated("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad");

        var admitted = KeyFor(Composed(delegation, actorTenant: "acme"));
        var refused = KeyFor(Composed(delegation, actorTenant: "globex"));

        refused.Should().NotBe(admitted,
            "a refused delegation holds no permissions, whatever it was answered while admitted");
    }

    /// <summary>A delegated caller whose authority is composed by the real decorator.</summary>
    private static DelegatedCaller Composed(IDelegationContext delegation, string actorTenant)
    {
        var caller = new DelegatedCaller(delegation, tenantId: "acme");
        caller.Authorization = new DelegatedUserAuthorization(
            new SubjectAuthority("rates.read", "rates.view-all"),
            caller,
            new ActorAuthority(tenantId: actorTenant, granted: "rates.view-all"));
        return caller;
    }

    private static string KeyFor(IDelegationContext delegation) => KeyFor(new DelegatedCaller(delegation));

    private static string KeyFor(ICurrentUser caller)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICacheStack, InertCache>()
            .AddSingleton(caller)
            .BuildServiceProvider();

        var entry = ActionCacheEntry.Open(new RatesAnswer(), services, onNoStackRegistered: () => { });

        entry.Should().NotBeNull();
        return entry!.Key;
    }

    private sealed class RatesAnswer : ICacheable
    {
        public string GetCacheKey() => "rates";
        public CacheEntryOptions GetCacheOptions() => new();
    }

    private sealed class Delegated(
        string subjectId, string actorId, DelegationPolicy policy, string? grantId) : IDelegationContext
    {
        public string SubjectId => subjectId;
        public string ActorId => actorId;
        public ActorKind ActorKind => ActorKind.Agent;
        public DelegationPolicy Policy => policy;
        public string? Purpose => null;
        public string? GrantId => grantId;
        public DateTimeOffset? ExpiresAt => null;
        public IReadOnlyList<string> Chain => [actorId];
    }

    private sealed class DelegatedCaller(IDelegationContext delegation, string? tenantId = null) : ICurrentUser
    {
        public string Id => delegation.SubjectId;
        public string? DisplayName => null;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => tenantId;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();
        public IDelegationContext? Delegation => delegation;
        public IUserAuthorization Authorization { get; set; } = NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    /// <summary>The subject's own authority: a fixed set of permissions.</summary>
    private sealed class SubjectAuthority(params string[] permissions) : IUserAuthorization
    {
        private readonly HashSet<string> _permissions = new(permissions, StringComparer.Ordinal);

        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions => _permissions;
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => _permissions.Contains(permission);
        public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(HasPermission);
        public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.All(HasPermission);
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }

    /// <summary>The actor's side: the grant's permissions and the tenant the actor belongs to.</summary>
    private sealed class ActorAuthority(string tenantId, params string[] granted) : IActorAuthorityResolver
    {
        public IReadOnlySet<string> ActorPermissions(IDelegationContext delegation)
            => new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlySet<string> GrantedPermissions(IDelegationContext delegation)
            => new HashSet<string>(granted, StringComparer.Ordinal);

        public string? ActorTenantId(IDelegationContext delegation) => tenantId;
    }

    /// <summary>Nothing is stored: the test reads the key the entry was opened with.</summary>
    private sealed class InertCache : ICacheStack
    {
        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => factory(ct);

        public async ValueTask<T> GetOrSetAsync<T>(string key,
            Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => (await factory(ct).ConfigureAwait(false)).Value;

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default) => new(default(T));

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
            => new((false, default));

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null,
            CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string key, CancellationToken ct = default) => ValueTask.CompletedTask;
    }
}
