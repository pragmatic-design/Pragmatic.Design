namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     When a filter is lifted — by the toggle, by the mode, by a bypass permission — and when a read
///     fails closed instead.
/// </summary>
public sealed partial class DefaultQueryFilterProvider
{
    /// <summary>
    ///     When <see cref="QueryFilterOptions.FailClosedWhenAnonymous"/> is enabled and
    ///     there is a permission-based filter for <paramref name="entityType"/> but no authenticated user
    ///     (and permission filters are not being intentionally skipped by an elevated mode), the query
    ///     must return no rows rather than silently dropping the permission filter.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the toggle alone. A filter can be lifted two ways — <c>Disable&lt;T&gt;()</c> on the
    ///     toggle, or its type in <see cref="FilterContext.DisabledFilters" />, which is how
    ///     <c>QueryBuilder.IgnoreQueryFilter&lt;T&gt;()</c> travels — and the composition honours both.
    ///     A guard that saw only the first would still find a lifted permission filter active, and the
    ///     read would come back empty: the fail-closed outcome, which is what a working guard looks
    ///     like.
    /// </remarks>
    private bool ShouldFailClosed(Type entityType, FilterContext filterContext)
    {
        if (!_options.FailClosedWhenAnonymous)
            return false;
        if (currentUser is { IsAuthenticated: true })
            return false;

        // Anonymous (or no user) AND at least one permission-based filter applies to THIS entity.
        // The type registry is passthrough (ImplementsFilterFor == true for all), so GetFiltersForType
        // returns every registered filter including other entities' ownership/scoped filters. Those are
        // discarded elsewhere by the entity-type guard; ShouldFailClosed must apply the SAME guard,
        // otherwise the mere existence of any owned/scoped entity in the app would fail-close reads of
        // unrelated tenant-only entities (e.g. Property).
        return GetFiltersForType(entityType)
            .Any(f => IsFor(f, entityType)
                      && f is IPermissionBasedFilter
                      && !IsLiftedByCaller(f, entityType, filterContext));
    }

    /// <summary>
    ///     Whether this filter is lifted in the current context — by the toggle, by the mode, or by a
    ///     bypass permission.
    /// </summary>
    /// <remarks>
    ///     One place, because the caller has to know <b>that</b> a filter was lifted regardless of
    ///     which of the reasons applied: an additive group is lifted whole or not at all, and the
    ///     reason does not change that.
    /// </remarks>
    private bool IsLifted(IQueryFilter filter, Type entityType, FilterContext filterContext)
        => IsLiftedByCaller(filter, entityType, filterContext) || IsPermissionBypassed(filter);

    /// <summary>
    ///     Whether the caller <b>asked</b> for this filter to be lifted — by either spelling of the
    ///     toggle, or by the mode.
    /// </summary>
    /// <remarks>
    ///     Kept apart from the permission bypass in <see cref="IsLifted" /> because the fail-closed
    ///     guard has to distinguish them: an anonymous caller bypasses every permission filter, and
    ///     that bypass is the very thing the guard exists to catch. Asking "is it lifted" there would
    ///     make the guard answer no every time — the opposite defect, and the worse one.
    /// </remarks>
    private bool IsLiftedByCaller(IQueryFilter filter, Type entityType, FilterContext filterContext)
    {
        // Toggle: disabled by type, or all disabled
        if (IsFilterDisabled(filter))
            return true;

        // FilterContext.DisabledFilters — by entity type or by filter type
        if (filterContext.DisabledFilters.Contains(entityType) ||
            filterContext.DisabledFilters.Contains(filter.GetType()))
            return true;

        // FilterMode: permission-based filters are lifted from Admin up
        if (filterContext.SkipPermissionBased && filter is IPermissionBasedFilter)
            return true;

        // FilterMode: tenant filters are lifted from Background up
        return filterContext.SkipTenant && IsTenantFilter(filter);
    }

    /// <summary>
    ///     Checks if a filter is a tenant filter via marker interface.
    /// </summary>
    private static bool IsTenantFilter(IQueryFilter filter)
        => filter is ITenantFilter;

    private bool IsFilterDisabled(IQueryFilter filter)
    {
        if (toggle is null)
            return false;
        if (toggle.AllDisabled)
            return true;
        return toggle.IsDisabled(filter.GetType());
    }

    /// <summary>
    ///     Permission-based filters are skipped when:
    ///     <list type="bullet">
    ///         <item>No <c>ICurrentUser</c> available (background job, seed, CLI tool)</item>
    ///         <item>User is not authenticated (anonymous request)</item>
    ///         <item>User has the bypass permission (e.g. admin override)</item>
    ///     </list>
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Design intent — anonymous bypass</b>: when no authenticated user is present,
    ///         permission-based filters are skipped entirely. This is safe because:
    ///     </para>
    ///     <list type="number">
    ///         <item>Public endpoints (<c>[AllowAnonymous]</c>) are opt-in — the dev explicitly chose to expose them.</item>
    ///         <item>Permission-based filters require a user context to evaluate (e.g. "show only MY orders").</item>
    ///         <item>Without a user, there is no meaningful permission scope to apply.</item>
    ///     </list>
    ///     <para>
    ///         If an endpoint is both anonymous AND needs data-level filtering, use a non-permission
    ///         filter (e.g. <c>IQueryFilter&lt;T&gt;</c> without <c>IPermissionBasedFilter</c>) or
    ///         restrict the endpoint to authenticated users via <c>[RequireAuthorization]</c>.
    ///     </para>
    /// </remarks>
    private bool IsPermissionBypassed(IQueryFilter filter)
    {
        if (filter is not IPermissionBasedFilter permFilter)
            return false;

        // Anonymous/unauthenticated: skip permission filters (no user context to evaluate)
        if (currentUser is null || !currentUser.IsAuthenticated)
            return true;

        // Mid-resolution: the permissions this filter would consult are the ones being resolved, so
        // asking for them yields the guard's empty set and the read comes back filtered by nothing
        // known. ⚠️ Leaving it to the provider is not enough: a provider that reads past the filter
        // pipeline with Disable<TFilter>() lifts one filter and leaves the others resolving.
        // Deciding it here is what makes the narrow form safe.
        if (currentUser.Authorization.IsResolvingPermissions)
            return true;

        return currentUser.Authorization.HasPermission(permFilter.BypassPermission);
    }
}
