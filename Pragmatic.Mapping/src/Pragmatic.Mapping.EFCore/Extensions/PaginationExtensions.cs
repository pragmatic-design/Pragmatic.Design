using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Pagination;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Mapping.EFCore.Extensions;

/// <summary>
///     Extension methods for paginated queries with DTO projection.
/// </summary>
public static class PaginationExtensions
{
    /// <param name="query">The source query.</param>
    /// <typeparam name="TSource">The source entity type.</typeparam>
    extension<TSource>(IQueryable<TSource> query)
    {
        /// <summary>
        ///     Projects and paginates the query, returning a page of DTOs.
        /// </summary>
        /// <typeparam name="TDto">The target DTO type.</typeparam>
        /// <param name="projection">The projection expression.</param>
        /// <param name="pageNumber">The page number (1-based).</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Page{T}" /> of the DTOs, with its position and the total count.</returns>
        /// <remarks>
        ///     This method executes two separate database round-trips: one <c>COUNT(*)</c> query
        ///     and one data query. This is the standard N=2 pattern for cursor-free pagination.
        ///     For scenarios where total count is not required, prefer <see cref="ToSliceDtoAsync{TSource,TDto}" />.
        ///     <para>
        ///         <b>Order the query first.</b> <c>Skip</c>/<c>Take</c> over an unordered query is
        ///         non-deterministic — the database may return rows in a different order across the two
        ///         round-trips, so a row can be duplicated or skipped between pages. Always apply an
        ///         <c>OrderBy</c> (on a stable, unique key) upstream of this call.
        ///     </para>
        /// </remarks>
        public async Task<Page<TDto>> ToPagedDtoAsync<TDto>(Expression<Func<TSource, TDto>> projection,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            Pragmatic.Ensure.Ensure.ThrowIfLessThan(pageNumber, 1);
            Pragmatic.Ensure.Ensure.ThrowIfLessThan(pageSize, 1);

            using var activity = MappingActivitySource.Instance.StartActivity("mapping.projection.to_paged");
            activity?.SetTag(MappingTags.SourceType, MappingTypeNameCache<TSource, TDto>.SourceName);
            activity?.SetTag(MappingTags.DtoType, MappingTypeNameCache<TSource, TDto>.DtoName);
            activity?.SetTag(MappingTags.PageNumber, pageNumber);
            activity?.SetTag(MappingTags.PageSize, pageSize);

            var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(projection)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            activity?.SetTag(MappingTags.TotalCount, totalCount);
            activity?.SetTag(MappingTags.ResultCount, items.Count);

            return new Page<TDto>(
                items,
                totalCount,
                pageNumber,
                pageSize);
        }

        /// <summary>
        ///     Projects and paginates the query using offset/limit, returning a page of DTOs.
        /// </summary>
        /// <typeparam name="TDto">The target DTO type.</typeparam>
        /// <param name="projection">The projection expression.</param>
        /// <param name="offset">The number of items to skip.</param>
        /// <param name="limit">The maximum number of items to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of projected DTOs.</returns>
        /// <remarks>
        ///     <b>Order the query first.</b> <c>Skip</c>/<c>Take</c> over an unordered query is
        ///     non-deterministic; apply an <c>OrderBy</c> on a stable, unique key upstream so pages do
        ///     not overlap or drop rows.
        /// </remarks>
        public async Task<List<TDto>> ToSliceDtoAsync<TDto>(Expression<Func<TSource, TDto>> projection,
            int offset,
            int limit,
            CancellationToken cancellationToken = default)
        {
            Pragmatic.Ensure.Ensure.ThrowIfLessThan(offset, 0);
            Pragmatic.Ensure.Ensure.ThrowIfLessThan(limit, 1);

            using var activity = MappingActivitySource.Instance.StartActivity("mapping.projection.to_slice");
            activity?.SetTag(MappingTags.SourceType, MappingTypeNameCache<TSource, TDto>.SourceName);
            activity?.SetTag(MappingTags.DtoType, MappingTypeNameCache<TSource, TDto>.DtoName);
            activity?.SetTag(MappingTags.Offset, offset);
            activity?.SetTag(MappingTags.Limit, limit);

            var result = await query
                .Skip(offset)
                .Take(limit)
                .Select(projection)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            activity?.SetTag(MappingTags.ResultCount, result.Count);

            return result;
        }
    }
}
