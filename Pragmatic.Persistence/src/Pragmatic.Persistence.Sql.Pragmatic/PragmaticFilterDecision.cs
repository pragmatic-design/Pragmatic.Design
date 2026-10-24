using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>
///     How Pragmatic decides a filter of its entities with EF Core, for either engine: the mode and the lifts of <see cref="IQueryFilterToggle" />,
///     the caller of <see cref="ICurrentUser" />, <see cref="QueryFilterOptions.FailClosedWhenAnonymous" />. The rules are
///     <c>DefaultQueryFilterProvider</c>'s (<c>.Lifting.cs</c>) for the filters it evaluates, and <c>EfCoreQueryExecutor</c>'s for the named
///     filters EF installs on the model: where a family is both (soft delete, tenant), the rows come back only when both halves are lifted.
///     Read by the Sql engine's bridge (<c>PragmaticSqlFilters</c>); it names no engine, so another engine's bridge decides the same way.
/// </summary>
internal sealed class PragmaticFilterDecision(ICurrentUser? currentUser, IQueryFilterToggle? toggle, QueryFilterOptions? options, IEnumerable<DataScopeRule>? scopeRules = null)
{
    private const string TenantQueryFilterName = "Tenant"; // EfCoreQueryExecutor.TenantQueryFilterName

    private readonly QueryFilterOptions _options = options ?? new QueryFilterOptions();
    private readonly DataScopeRule[] _scopeRules = scopeRules?.ToArray() ?? [];

    /// <summary>The data scope rules registered (<c>AddDataScopeRule</c>): what the computed scopes read and the saves materialize.</summary>
    public IReadOnlyList<DataScopeRule> ScopeRules => _scopeRules;

    /// <summary>
    ///     A computed scope (<c>ComputedScopeFilter</c>): the rule of type <paramref name="rule" /> applies when it is registered, Computed or Hybrid,
    ///     and the caller holds its scope (<c>scope:{ScopeName}</c>); otherwise it adds nothing to the scopes' OR (closed). The group's lifts are
    ///     the scope filter's, around it.
    /// </summary>
    public PragmaticFilterState ComputedScopeState(Type? rule, IReadOnlyCollection<string> scopes)
    {
        var registered = Array.Find(_scopeRules, r => r.GetType() == rule);
        return registered is { Strategy: ScopeStrategy.Computed or ScopeStrategy.Hybrid } && scopes.Contains(ScopeIdentifiers.ForScope(registered.ScopeName))
            ? PragmaticFilterState.Active
            : PragmaticFilterState.Closed;
    }

    /// <summary>A Computed or Hybrid rule registered for <paramref name="entity" /> that no generator compiled: refused, not left out of the read.</summary>
    public void EnsureCompiled(Type entity)
    {
        foreach (var rule in _scopeRules)
        {
            if (rule.EntityType == entity && rule.Strategy is ScopeStrategy.Computed or ScopeStrategy.Hybrid && !CompiledScopeRules.IsCompiled(rule.GetType()))
                throw CompiledScopeRules.NotCompiled(rule);
        }
    }

    /// <summary>The mode in effect: the toggle's, Normal without one.</summary>
    public FilterMode Mode => toggle?.CurrentMode ?? FilterMode.Normal;

    public PragmaticFilterState StateOf(PragmaticFamily family, Type entity, Type? filter, string? name, string? bypass)
    {
        var mode = Mode;
        if (mode == FilterMode.Raw)
            return PragmaticFilterState.Lifted; // no automatic filter, named ones included
        return family switch
        {
            // [SoftDelete] is a provider filter and the EF named filter "SoftDelete": the rows come back when both are lifted
            // ([WithoutFilter<Entity>] lifts both; Disable<SoftDeleteFilter>() alone leaves EF's).
            PragmaticFamily.SoftDelete => LiftedByCaller(entity, filter) && LiftedByName(name) ? PragmaticFilterState.Lifted : PragmaticFilterState.Active,
            // The tenant: Background lifts both halves; otherwise the provider's and the named "Tenant" together.
            PragmaticFamily.Tenant => mode >= FilterMode.Background || (LiftedByCaller(entity, filter) && LiftedByName(name ?? TenantQueryFilterName))
                ? PragmaticFilterState.Lifted
                : PragmaticFilterState.Active,
            PragmaticFamily.Permission => PermissionState(entity, filter, bypass, mode),
            // [TemporalRelation]'s TemporalFilter is a plain provider filter (not permission-based, not a tenant's, not an EF named filter):
            // Admin and Background leave it, the caller's lifts (IncludeHistory, [WithoutFilter<T>], DisableAll) remove it.
            PragmaticFamily.Temporal => LiftedByCaller(entity, filter) ? PragmaticFilterState.Lifted : PragmaticFilterState.Active,
            // A computed scope needs the caller's scopes: the bridges ask ComputedScopeState. Asked here, it adds nothing.
            PragmaticFamily.ComputedScope => PragmaticFilterState.Closed,
            // A declared rule is an EF named filter only: Raw or its name lifts it, a mode does not.
            _ => LiftedByName(name) ? PragmaticFilterState.Lifted : PragmaticFilterState.Active,
        };
    }

    /// <summary>
    ///     Ownership, scopes and both together (<c>IPermissionBasedFilter</c>): lifted by the caller (toggle, disabled types, Admin and up),
    ///     closed for an anonymous caller when <see cref="QueryFilterOptions.FailClosedWhenAnonymous" />, else lifted for one; lifted while
    ///     the permissions are being resolved and for a caller holding the bypass permission (<c>DefaultQueryFilterProvider</c>:
    ///     <c>ShouldFailClosed</c>, <c>IsLiftedByCaller</c>, <c>IsPermissionBypassed</c>).
    /// </summary>
    private PragmaticFilterState PermissionState(Type entity, Type? filter, string? bypass, FilterMode mode)
    {
        if (mode >= FilterMode.Admin || LiftedByCaller(entity, filter))
            return PragmaticFilterState.Lifted;
        if (currentUser is not { IsAuthenticated: true })
            return _options.FailClosedWhenAnonymous ? PragmaticFilterState.Closed : PragmaticFilterState.Lifted;
        if (currentUser.Authorization.IsResolvingPermissions)
            return PragmaticFilterState.Lifted;
        return bypass is not null && currentUser.Authorization.HasPermission(bypass) ? PragmaticFilterState.Lifted : PragmaticFilterState.Active;
    }

    /// <summary>The provider's half: every filter disabled, the filter's type disabled, or the entity's.</summary>
    private bool LiftedByCaller(Type entity, Type? filter)
    {
        if (toggle is null)
            return false;
        if (toggle.AllDisabled || (filter is not null && toggle.IsDisabled(filter)))
            return true;
        var disabled = toggle.GetDisabledFilterTypes();
        return disabled.Contains(entity) || (filter is not null && disabled.Contains(filter));
    }

    /// <summary>The named half: EF's <c>IgnoreQueryFilters(names)</c>, from the toggle's lifted names.</summary>
    private bool LiftedByName(string? name)
        => name is not null && toggle is not null && toggle.GetDisabledQueryFilterNames().Contains(name);
}
