using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Persistence.Query.Executors;

/// <summary>
///     In-memory query executor for LINQ-to-Objects. Suitable for testing and small datasets.
/// </summary>
/// <remarks>
///     <b>SECURITY — no global query filters.</b> Unlike the EF Core executor, this executor
///     applies ONLY the query's own <c>Apply</c> logic; it does NOT apply the global query filters
///     (soft-delete, tenant, ownership, scoped visibility). A soft-deleted or cross-tenant/cross-user
///     row present in the in-memory source is returned. Use it only for tests or genuinely public,
///     unfiltered data — never as the executor for an <c>[HasOwner]</c>/<c>[HasAccessScopes]</c>/
///     <c>ITenantEntity</c>/soft-deleted entity where record-level isolation is required.
/// </remarks>
public sealed class InMemoryQueryExecutor : IQueryExecutor
{
    /// <summary>
    ///     Default singleton instance.
    /// </summary>
    public static InMemoryQueryExecutor Instance { get; } = new();

    /// <inheritdoc />
    public Task<PagedResult<TEntity>> ExecuteAsync<TEntity>(
        IPagedQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        try
        {
            // Apply query and materialize ONCE to avoid re-running the LINQ pipeline twice
            // (one full scan for Count(), another for Skip/Take) on an in-memory source.
            var filtered = query.Apply(source).ToList();

            var totalCount = filtered.Count;

            // Page in memory over the already-materialized list.
            var items = filtered
                .Skip(query.Skip)
                .Take(query.Take)
                .ToList();

            return Task.FromResult(PagedResult<TEntity>.Success(
                items,
                totalCount,
                query.Page,
                query.PageSize));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PagedResult<TEntity>.Failure(
                new QueryError.Database { Message = ex.Message, Inner = ex }));
        }
    }

    /// <inheritdoc />
    public Task<PagedResult<TResult>> ExecuteAsync<TEntity, TResult>(
        IPagedQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class
    {
        try
        {
            // An aggregate read is counted and paged over its own rows: the page of a grouping is a
            // page of groups, and the entities behind them are not what the caller receives.
            if (query.Aggregate is { } aggregate)
            {
                var grouped = aggregate(query.Apply(source)).ToList();

                return Task.FromResult(PagedResult<TResult>.Success(
                    grouped.Skip(query.Skip).Take(query.Take).ToList(),
                    grouped.Count,
                    query.Page,
                    query.PageSize));
            }

            // Apply query and materialize ONCE to avoid re-running the LINQ pipeline twice
            // (one full scan for Count(), another for Skip/Take) on an in-memory source.
            var filtered = query.Apply(source).ToList();

            var totalCount = filtered.Count;

            // Page in memory over the already-materialized list.
            var paged = filtered
                .Skip(query.Skip)
                .Take(query.Take);

            // Apply projection if available
            IReadOnlyList<TResult> items;
            if (query.Projection != null)
            {
                items = paged.Select(query.Projection.Compile()).ToList();
            }
            else
            {
                // No projection — TEntity must be assignable to TResult; fail gracefully if not
                items = paged.OfType<TResult>().ToList();
            }

            return Task.FromResult(PagedResult<TResult>.Success(
                items,
                totalCount,
                query.Page,
                query.PageSize));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PagedResult<TResult>.Failure(
                new QueryError.Database { Message = ex.Message, Inner = ex }));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> ExecuteAllAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        var filtered = query.Apply(source);
        return Task.FromResult<IReadOnlyList<TEntity>>(filtered.ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TResult>> ExecuteAllAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class
    {
        var filtered = query.Apply(source);
        if (query.Aggregate is { } aggregate)
            return Task.FromResult<IReadOnlyList<TResult>>(aggregate(filtered).ToList());

        IReadOnlyList<TResult> items = query.Projection != null
            ? filtered.Select(query.Projection.Compile()).ToList()
            : filtered.OfType<TResult>().ToList();
        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public Task<Result<TEntity>> ExecuteSingleAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        var entity = query.Apply(source).FirstOrDefault();
        return Task.FromResult(entity is null
            ? Result<TEntity>.Failure(NotFoundError.For(typeof(TEntity).Name))
            : Result<TEntity>.Success(entity));
    }

    /// <inheritdoc />
    public Task<Result<TResult>> ExecuteSingleAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class
    {
        var filtered = query.Apply(source);
        if (query.Aggregate is { } aggregate)
        {
            var group = aggregate(filtered).FirstOrDefault();

            return Task.FromResult(group is null
                ? Result<TResult>.Failure(NotFoundError.For(typeof(TEntity).Name))
                : Result<TResult>.Success(group));
        }

        var result = query.Projection != null
            ? filtered.Select(query.Projection.Compile()).FirstOrDefault()
            : filtered.OfType<TResult>().FirstOrDefault();

        return Task.FromResult(result is null
            ? Result<TResult>.Failure(NotFoundError.For(typeof(TEntity).Name))
            : Result<TResult>.Success(result));
    }
}
