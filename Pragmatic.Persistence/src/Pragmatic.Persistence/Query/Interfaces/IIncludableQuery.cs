namespace Pragmatic.Persistence.Query.Interfaces;

/// <summary>
///     Query that declares navigation paths for eager loading.
///     The query executor translates these to provider-specific include operations
///     (e.g., EF Core <c>Include()</c> / <c>ThenInclude()</c>).
/// </summary>
/// <typeparam name="TEntity">The root entity type.</typeparam>
/// <remarks>
///     <para>
///         Paths use dot notation for nested navigations:
///         <c>"Customer"</c> or <c>"Lines.Product"</c>.
///     </para>
///     <para>
///         This is a mixin interface — combine with <see cref="IQuery{TEntity}"/>
///         or <see cref="IPagedQuery{TEntity}"/> as needed.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public class OrderDetailQuery : IPagedQuery&lt;Order&gt;, IIncludableQuery&lt;Order&gt;
/// {
///     public int Page { get; init; }
///     public int PageSize { get; init; }
///     public IReadOnlyList&lt;string&gt; IncludePaths =&gt; ["Customer", "Lines.Product"];
///
///     public IQueryable&lt;Order&gt; Apply(IQueryable&lt;Order&gt; query)
///         =&gt; query.OrderBy(o =&gt; o.OrderNumber);
/// }
/// </code>
/// </example>
public interface IIncludableQuery<TEntity> where TEntity : class
{
    /// <summary>
    ///     Navigation paths to eagerly load.
    ///     Dot-separated paths are supported for nested navigations.
    /// </summary>
    IReadOnlyList<string> IncludePaths { get; }
}
