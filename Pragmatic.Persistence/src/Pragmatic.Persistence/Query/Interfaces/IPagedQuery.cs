namespace Pragmatic.Persistence.Query.Interfaces;

/// <summary>
///     Query with built-in pagination support.
/// </summary>
/// <typeparam name="TEntity">The source entity type.</typeparam>
public interface IPagedQuery<TEntity> : IQuery<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     The current page number (1-based).
    /// </summary>
    int Page { get; }

    /// <summary>
    ///     The number of items per page.
    /// </summary>
    int PageSize { get; }

    /// <summary>
    ///     The number of items to skip for offset-based pagination.
    /// </summary>
    int Skip => Page > 0 && PageSize > 0 ? (Page - 1) * PageSize : 0;

    /// <summary>
    ///     The number of items to take.
    /// </summary>
    int Take => PageSize;
}

/// <summary>
///     Paged query with projection to a result type.
/// </summary>
/// <typeparam name="TEntity">The source entity type.</typeparam>
/// <typeparam name="TResult">The projected result type (DTO).</typeparam>
public interface IPagedQuery<TEntity, TResult> : IQuery<TEntity, TResult>, IPagedQuery<TEntity>
    where TEntity : class
    where TResult : class
{
}
