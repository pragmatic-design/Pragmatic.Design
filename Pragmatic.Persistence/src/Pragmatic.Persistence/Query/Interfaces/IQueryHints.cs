namespace Pragmatic.Persistence.Query.Interfaces;

/// <summary>
///     Query execution hints that influence how the provider processes the query.
///     Implementations provide sensible defaults — override only what you need.
/// </summary>
/// <remarks>
///     <para>
///         This is a mixin interface — combine with <see cref="IQuery{TEntity}"/>
///         or <see cref="IPagedQuery{TEntity}"/> as needed.
///     </para>
///     <para>
///         When no hints are specified (query doesn't implement <see cref="IQueryHints"/>),
///         the executor uses its own defaults (typically read-only / no-tracking).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Read-only query with split execution for large collections
/// public class OrdersQuery : IPagedQuery&lt;Order&gt;, IQueryHints
/// {
///     public bool SplitQuery =&gt; true;
///     // NoTracking defaults to true, IgnoreGlobalFilters defaults to false
/// }
///
/// // Tracked query for subsequent updates
/// public class EditableOrderQuery : IQuery&lt;Order&gt;, IQueryHints
/// {
///     public bool NoTracking =&gt; false;
/// }
/// </code>
/// </example>
public interface IQueryHints
{
    /// <summary>
    ///     When true, entities are not tracked by the change tracker.
    ///     Default: <c>true</c> (read-only queries are the common case).
    /// </summary>
    bool NoTracking => true;

    /// <summary>
    ///     When true, uses split queries for collection navigations
    ///     to avoid cartesian explosion. Default: <c>false</c>.
    /// </summary>
    bool SplitQuery => false;

    /// <summary>
    ///     When true, bypasses global query filters (e.g., soft-delete, tenant).
    ///     Default: <c>false</c>.
    /// </summary>
    bool IgnoreGlobalFilters => false;
}
