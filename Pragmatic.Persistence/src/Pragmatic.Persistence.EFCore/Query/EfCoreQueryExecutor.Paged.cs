using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Diagnostics;
using Pragmatic.Persistence.Query.Executors;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>The paged reads: a count and a page, instrumented, with a failure mapped to a query error.</summary>
public sealed partial class EfCoreQueryExecutor
{
    // =========================================================================
    // Internal execution (separated for cache wrapper pattern)
    // =========================================================================

    private async Task<PagedResult<TEntity>> ExecutePagedInternalAsync<TEntity>(
        IPagedQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken) where TEntity : class
    {
        var queryName = query.GetType().Name;
        using var activity = PersistenceDiagnostics.ActivitySource.StartActivity($"Query.{queryName}");
        activity?.SetTag(DbTags.Operation, "SELECT");
        activity?.SetTag(DbTags.CollectionName, typeof(TEntity).Name);

        PersistenceDiagnostics.QueriesExecuted.Add(1,
            new KeyValuePair<string, object?>("query.name", queryName));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var queryable = PrepareSource(source, query);
            var filtered = query.Apply(queryable);
            filtered = ApplyGlobalFilters(filtered, query);

            var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

            var items = await filtered
                .Skip(query.Skip)
                .Take(query.Take)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            PersistenceDiagnostics.QueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("query.name", queryName),
                new KeyValuePair<string, object?>("query.result", "success"));
            activity?.SetTag(DbTags.RowsAffected, totalCount);
            activity?.SetSuccess();

            LogQueryCompleted(queryName, totalCount, query.Page, query.PageSize);
            return PagedResult<TEntity>.Success(items, totalCount, query.Page, query.PageSize);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PersistenceDiagnostics.QueryFailures.Add(1,
                new KeyValuePair<string, object?>("query.name", queryName));
            PersistenceDiagnostics.QueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("query.name", queryName),
                new KeyValuePair<string, object?>("query.result", "failure"));
            activity?.RecordException(ex);

            var queryError = MapToQueryError(ex);
            LogQueryFailed(queryName, queryError.GetType().Name, ex);
            return PagedResult<TEntity>.Failure(queryError);
        }
    }

    private async Task<PagedResult<TResult>> ExecutePagedProjectedInternalAsync<TEntity, TResult>(
        IPagedQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken)
        where TEntity : class
        where TResult : class
    {
        var queryName = query.GetType().Name;
        using var activity = PersistenceDiagnostics.ActivitySource.StartActivity($"Query.{queryName}");
        activity?.SetTag(DbTags.Operation, "SELECT");
        activity?.SetTag(DbTags.CollectionName, typeof(TEntity).Name);

        PersistenceDiagnostics.QueriesExecuted.Add(1,
            new KeyValuePair<string, object?>("query.name", queryName));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var queryable = PrepareSource(source, query);
            var filtered = query.Apply(queryable);
            filtered = ApplyGlobalFilters(filtered, query, navigation: false);

            // ⚠️ An aggregate read is counted and paged over its own rows, not over the entities it
            // groups: the page of a grouping is a page of groups, and counting the entities would
            // report a total no page of this result could ever reach.
            if (query.Aggregate is not null)
            {
                var grouped = ApplyNavigationFilters(Shaped(query, filtered)!, query);
                var groups = await grouped.CountAsync(cancellationToken).ConfigureAwait(false);
                var page = await grouped
                    .Skip(query.Skip)
                    .Take(query.Take)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return PagedResult<TResult>.Success(page, groups, query.Page, query.PageSize);
            }

            var totalCount = await ApplyNavigationFilters(filtered, query).CountAsync(cancellationToken).ConfigureAwait(false);

            var unfilteredPage = filtered
                .Skip(query.Skip)
                .Take(query.Take);
            var paged = ApplyNavigationFilters(unfilteredPage, query);

            IReadOnlyList<TResult> items;
            if (query.Projection is not null)
            {
                items = await ApplyNavigationFilters(unfilteredPage.Select(query.Projection), query)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (query.MapEach is { } map)
            {
                // The count and the page are already server-side; only the projection moves.
                var rows = await paged.ToListAsync(cancellationToken).ConfigureAwait(false);
                items = rows.Select(map).ToList();
            }
            else
            {
                items = await paged
                    .Cast<TResult>()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            stopwatch.Stop();
            PersistenceDiagnostics.QueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("query.name", queryName),
                new KeyValuePair<string, object?>("query.result", "success"));
            activity?.SetTag(DbTags.RowsAffected, totalCount);
            activity?.SetSuccess();

            LogQueryCompleted(queryName, totalCount, query.Page, query.PageSize);
            return PagedResult<TResult>.Success(items, totalCount, query.Page, query.PageSize);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PersistenceDiagnostics.QueryFailures.Add(1,
                new KeyValuePair<string, object?>("query.name", queryName));
            PersistenceDiagnostics.QueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("query.name", queryName),
                new KeyValuePair<string, object?>("query.result", "failure"));
            activity?.RecordException(ex);

            var queryError = MapToQueryError(ex);
            LogQueryFailed(queryName, queryError.GetType().Name, ex);
            return PagedResult<TResult>.Failure(queryError);
        }
    }
}
