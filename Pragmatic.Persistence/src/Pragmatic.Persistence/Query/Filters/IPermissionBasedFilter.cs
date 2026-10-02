namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Non-generic marker for permission-based query filters.
///     Allows <see cref="DefaultQueryFilterProvider"/> to check permissions without knowing T.
/// </summary>
public interface IPermissionBasedFilter : IQueryFilter
{
    /// <summary>
    ///     The permission that bypasses this filter.
    ///     Users with this permission see all records (no filter applied).
    /// </summary>
    string BypassPermission { get; }
}

/// <summary>
///     Query filter that depends on user permissions.
///     Automatically skipped when no <c>ICurrentUser</c> is available (background jobs, seed)
///     or when the user has the <see cref="IPermissionBasedFilter.BypassPermission"/>.
/// </summary>
/// <typeparam name="T">The entity type to filter.</typeparam>
/// <remarks>
///     <para>
///         Use for row-level security filters (e.g., "only see orders from my team").
///         The <see cref="DefaultQueryFilterProvider"/> checks the permission and skips
///         the filter if the user has the bypass permission or if no user context exists.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public class TeamOrdersFilter(ICurrentUser user) : IPermissionBasedFilter&lt;Order&gt;
/// {
///     public string BypassPermission =&gt; "orders.view_all";
///     public int Priority =&gt; 300;
///
///     public Expression&lt;Func&lt;Order, bool&gt;&gt; GetFilter()
///         =&gt; order =&gt; order.TeamId == user.Claims["team_id"];
/// }
/// </code>
/// </example>
public interface IPermissionBasedFilter<T> : IQueryFilter<T>, IPermissionBasedFilter where T : class
{
}
