using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Result;

namespace Pragmatic.Persistence.Query.Executors;

/// <summary>
///     Executes queries and returns paged results.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    ///     Executes a query returning the entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="source">The source queryable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paged result with the entities.</returns>
    Task<PagedResult<TEntity>> ExecuteAsync<TEntity>(
        IPagedQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    ///     Executes a query with projection.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="source">The source queryable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paged result with the projected results.</returns>
    Task<PagedResult<TResult>> ExecuteAsync<TEntity, TResult>(
        IPagedQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class;

    /// <summary>
    ///     Executes a query and returns all results (no paging).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="source">The source queryable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All matching entities.</returns>
    Task<IReadOnlyList<TEntity>> ExecuteAllAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    ///     Executes a projected query and returns all results (no paging), applying the query's projection.
    ///     This is the unpaged counterpart to the paged projected overload — used by published read contracts
    ///     (<c>[Published]</c>) that expose a query's DTOs across boundaries.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TResult">The projected result (DTO) type.</typeparam>
    /// <param name="query">The projected query to execute.</param>
    /// <param name="source">The source queryable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All matching projected results.</returns>
    Task<IReadOnlyList<TResult>> ExecuteAllAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class;

    /// <summary>
    ///     Executes a query expected to yield at most one row, and fails with a
    ///     <c>NotFoundError</c> when it yields none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Reading one record is the most common thing an application does, and it was the only
    ///         one of the four read/write shapes with no declarative home: paging had
    ///         <see cref="ExecuteAsync{TEntity}" />, lists had <see cref="ExecuteAllAsync{TEntity}" />,
    ///         and a single row meant dropping to <c>IReadRepository.GetByIdAsync</c> inside a
    ///         hand-written endpoint. The asymmetry was not a decision, it was an omission.
    ///     </para>
    ///     <para>
    ///         <b>First, not Single.</b> The query decides the filter and the ordering; the executor
    ///         does not second-guess it. A query written to match one row and matching two is a bug in
    ///         the query, and turning it into a runtime exception here would punish the legitimate
    ///         "the most recent one" shape.
    ///     </para>
    /// </remarks>
    Task<Result<TEntity>> ExecuteSingleAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <inheritdoc cref="ExecuteSingleAsync{TEntity}" />
    Task<Result<TResult>> ExecuteSingleAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class;
}
