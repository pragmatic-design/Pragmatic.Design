using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Delegation;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Filters;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Cache keys must partition by every
///     input that shapes query results: tenant, filter mode, disabled
///     filters, and user (when permission-based filters are active).
///     Without these partitions, a privileged query can poison the cache
///     for a subsequent normal-scope request.
/// </summary>
public class EfCoreQueryExecutorCacheKeyTests
{
    [Fact]
    public void Tenant_Partitions_CacheKey()
    {
        var acme = Build(tenantId: "acme");
        var baz = Build(tenantId: "baz");

        acme.BuildCacheKey<Marker>("q").Should().NotBe(baz.BuildCacheKey<Marker>("q"));
    }

    [Fact]
    public void FilterMode_Partitions_CacheKey()
    {
        var normal = Build(mode: FilterMode.Normal);
        var admin = Build(mode: FilterMode.Admin);

        normal.BuildCacheKey<Marker>("q").Should().NotBe(admin.BuildCacheKey<Marker>("q"));
    }

    [Fact]
    public void DisabledFilters_Partitions_CacheKey()
    {
        var noop = Build();
        var disabled = Build(disabled: [typeof(SoftDeleteMarker)]);

        noop.BuildCacheKey<Marker>("q").Should().NotBe(disabled.BuildCacheKey<Marker>("q"));
    }

    [Fact]
    public void PermissionBasedFilter_PartitionsByUser()
    {
        var alice = Build(userId: "alice", hasPermissionFilter: true);
        var bob = Build(userId: "bob", hasPermissionFilter: true);

        alice.BuildCacheKey<Marker>("q").Should().NotBe(bob.BuildCacheKey<Marker>("q"),
            "two users can legitimately see different rows under a permission-based filter");
    }

    /// <summary>
    ///     Under <see cref="DelegationPolicy.GrantScoped" /> the authority is the subject's permissions
    ///     intersected with what the grant names, so two grants of one actor for one subject can hold
    ///     different permissions — and a permission-based filter reads exactly that to decide its bypass.
    ///     The permission resolver keys on the grant for this reason; the rows computed under one grant
    ///     must not be served to the other.
    /// </summary>
    [Fact]
    public void GrantScopedDelegation_PartitionsByGrant()
    {
        var broadGrant = Build(userId: "ada", hasPermissionFilter: true,
            delegation: new StubDelegation("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad"));
        var narrowGrant = Build(userId: "ada", hasPermissionFilter: true,
            delegation: new StubDelegation("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-narrow"));

        broadGrant.BuildCacheKey<Marker>("q").Should().NotBe(narrowGrant.BuildCacheKey<Marker>("q"),
            "two grants can carry different permissions for the same subject and actor");
    }

    /// <summary>The control: the same grant is the same authority, and shares its entry.</summary>
    [Fact]
    public void GrantScopedDelegation_SameGrant_SharesTheKey()
    {
        var first = Build(userId: "ada", hasPermissionFilter: true,
            delegation: new StubDelegation("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad"));
        var second = Build(userId: "ada", hasPermissionFilter: true,
            delegation: new StubDelegation("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad"));

        first.BuildCacheKey<Marker>("q").Should().Be(second.BuildCacheKey<Marker>("q"));
    }

    /// <summary>
    ///     A refused delegation reads with no permissions — the filter's bypass does not apply — and it is
    ///     refused without its identifiers changing (expiry, an actor of another tenant). The rows the
    ///     admitted delegation read through the bypass must not reach it. Cross-tenant stands in for
    ///     expiry because it refuses with the delegation context identical on both sides.
    /// </summary>
    [Fact]
    public void ARefusedDelegation_DoesNotShareTheAdmittedOnesKey()
    {
        var delegation = new StubDelegation("ada", "agent-7", DelegationPolicy.GrantScoped, "grant-broad");

        var admitted = BuildFor(Composed(delegation, actorTenant: "acme"));
        var refused = BuildFor(Composed(delegation, actorTenant: "globex"));

        refused.BuildCacheKey<Marker>("q").Should().NotBe(admitted.BuildCacheKey<Marker>("q"),
            "the admitted delegation bypasses the filter and the refused one does not");
    }

    [Fact]
    public void NoDiscriminators_KeepsRawKey()
    {
        var executor = Build();
        executor.BuildCacheKey<Marker>("q").Should().Be("q");
    }

    // -------------------------------------------------------------------------

    private static EfCoreQueryExecutor Build(
        string? tenantId = null,
        FilterMode mode = FilterMode.Normal,
        Type[]? disabled = null,
        string? userId = null,
        bool hasPermissionFilter = false,
        IDelegationContext? delegation = null)
    {
        var toggle = (mode != FilterMode.Normal || (disabled is { Length: > 0 }))
            ? new StubToggle(mode, disabled ?? [])
            : null;
        var provider = hasPermissionFilter ? new StubProvider() : null;
        var tenant = tenantId is not null ? new StubTenantContext(tenantId) : null;
        var user = userId is not null ? new StubUser(userId, delegation) : null;

        return new EfCoreQueryExecutor(
            filterProvider: provider,
            filterMapComposer: null,
            filterToggle: toggle,
            cacheStack: null,
            logger: null,
            tenantContext: tenant,
            currentUser: user);
    }

    private static EfCoreQueryExecutor BuildFor(ICurrentUser user)
        => new(
            filterProvider: new StubProvider(),
            filterMapComposer: null,
            filterToggle: null,
            cacheStack: null,
            logger: null,
            tenantContext: null,
            currentUser: user);

    /// <summary>A delegated caller whose authority is composed by the real decorator.</summary>
    private static StubUser Composed(IDelegationContext delegation, string actorTenant)
    {
        var user = new StubUser(delegation.SubjectId, delegation, tenantId: "acme");
        user.Authorization = new DelegatedUserAuthorization(
            new SubjectAuthority("marker.read", "marker.view_all"),
            user,
            new ActorAuthority(tenantId: actorTenant, granted: "marker.view_all"));
        return user;
    }

    private sealed class Marker { }
    private sealed class SoftDeleteMarker : IQueryFilter<Marker>
    {
        public Expression<Func<Marker, bool>> GetFilter() => _ => true;
    }

    private sealed class StubToggle(FilterMode mode, Type[] disabled) : IQueryFilterToggle
    {
        public IDisposable Disable<TFilter>() where TFilter : IQueryFilter => new EmptyScope();
        public IDisposable Disable(Type filterType) => new EmptyScope();
        public IDisposable DisableAll() => new EmptyScope();
        public bool IsDisabled<TFilter>() where TFilter : IQueryFilter => disabled.Contains(typeof(TFilter));
        public bool IsDisabled(Type filterType) => disabled.Contains(filterType);
        public bool AllDisabled => false;
        public IReadOnlySet<Type> GetDisabledFilterTypes() => new HashSet<Type>(disabled);
        public IDisposable UseMode(FilterMode newMode) => new EmptyScope();
        public FilterMode CurrentMode => mode;

        private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    }

    private sealed class StubProvider : IQueryFilterProvider
    {
        public IEnumerable<IQueryFilter<T>> GetFilters<T>() where T : class
        {
            if (typeof(T) == typeof(Marker))
                yield return (IQueryFilter<T>)(object)new PermFilter();
        }

        public Expression<Func<T, bool>>? GetCombinedFilter<T>(NavigationContext? context = null) where T : class => null;
        public Expression<Func<T, bool>>? GetCombinedFilter<T>(FilterContext filterContext, NavigationContext? navigationContext = null) where T : class => null;
        public LambdaExpression? GetCombinedFilter(
            Type entityType, FilterContext filterContext, NavigationContext? navigationContext = null) => null;

        public bool HasFilters<T>() where T : class => true;

        private sealed class PermFilter : IPermissionBasedFilter<Marker>
        {
            public string BypassPermission => "marker.view_all";
            public Expression<Func<Marker, bool>> GetFilter() => _ => true;
        }
    }

    private sealed class StubTenantContext(string tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;
        public string? TenantName => null;
        public bool IsResolved => TenantId is not null;
    }

    private sealed class StubDelegation(
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

    private sealed class StubUser(string id, IDelegationContext? delegation, string? tenantId = null) : ICurrentUser
    {
        public string Id => id;
        public IDelegationContext? Delegation => delegation;
        public string? DisplayName => null;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => tenantId;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization { get; set; } = NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

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

    private sealed class ActorAuthority(string tenantId, params string[] granted) : IActorAuthorityResolver
    {
        public IReadOnlySet<string> ActorPermissions(IDelegationContext delegation)
            => new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlySet<string> GrantedPermissions(IDelegationContext delegation)
            => new HashSet<string>(granted, StringComparer.Ordinal);

        public string? ActorTenantId(IDelegationContext delegation) => tenantId;
    }
}
