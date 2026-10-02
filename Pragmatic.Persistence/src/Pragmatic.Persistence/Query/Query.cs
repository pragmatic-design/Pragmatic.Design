using Pragmatic.Persistence.Query.Builder;

namespace Pragmatic.Persistence.Query;

/// <summary>
///     Entry point for fluent query building.
/// </summary>
/// <example>
///     <code>
/// var query = Query.For&lt;User&gt;()
///     .WithFilter(u =&gt; u.IsActive)
///     .OrderBy(u =&gt; u.Name)
///     .WithPaging(page, pageSize);
///
/// var result = query.Build(db.Users);
/// </code>
/// </example>
public static class Query
{
    /// <summary>
    ///     Creates a new query builder for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to query.</typeparam>
    /// <returns>A new query builder.</returns>
    public static QueryBuilder<TEntity> For<TEntity>() where TEntity : class
        => new();
}
