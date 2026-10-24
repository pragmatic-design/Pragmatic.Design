using Pragmatic.Persistence.Query.Results;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Specification;

namespace PragmaticEntities.Tests;

/// <summary>
///     What Pragmatic's templates emit around a read, in the form they take on the Sql engine: the statements of <c>LoadEntityTemplate</c>
///     (<c>[LoadEntity]</c>, <c>[RequireExists]</c>, <c>[LoadEntities]</c>) and the read of <c>QueryInvokerTemplate</c>, each as the template writes
///     it today with the engine's call in place of EF Core's. The validation and the permission around them are the invoker base's and do not
///     change with the engine.
/// </summary>
/// <remarks>
///     TODO: written by hand. The templates emit these reads on EF Core only, so this suite writes them in the form they take on the Sql
///     engine, and the Sql generation compiles their <c>Spec&lt;T&gt;.Where</c> leaves as any application code. They go when the templates
///     emit the same calls for every engine (step 8 of the persistence engines' port, the EF-bound templates).
/// </remarks>
internal static class ReviewLoads
{
    /// <summary><c>[LoadEntity]</c> by key, no include: the repository's <c>GetByIdAsync</c>, as the template writes it.</summary>
    public static async Task<Result<Review, IError>> LoadAsync(IReadRepository<Review> reviews, Guid reviewId, CancellationToken ct = default)
    {
        var review = await reviews.GetByIdAsync(reviewId, ct).ConfigureAwait(false);
        if (review is null)
            return Result<Review, IError>.Failure(NotFoundError.For("Review", reviewId.ToString()));
        return Result<Review, IError>.Success(review);
    }

    /// <summary><c>[RequireExists]</c>: an EXISTS through the repository, the template's specification as it writes it.</summary>
    public static async Task<IError?> RequireExistsAsync(IReadRepository<Review> reviews, Guid reviewId, CancellationToken ct = default)
    {
        if (!await reviews.ExistsAsync(Spec<Review>.Where(__row => __row.PersistenceId.Equals(reviewId)), ct).ConfigureAwait(false))
            return NotFoundError.For("Review", reviewId.ToString());
        return null;
    }

    /// <summary><c>[LoadEntities]</c>: one query for the distinct keys, one 404 naming every key no row answered, the rows in the keys' order.</summary>
    public static async Task<Result<IReadOnlyList<Review>, IError>> LoadManyAsync(IReadRepository<Review> repository, IEnumerable<Guid>? reviewIds, CancellationToken ct = default)
    {
        IEnumerable<Guid>? __reviewsGiven = reviewIds;
        Guid[] __reviewsKeys = __reviewsGiven is null ? [] : Enumerable.ToArray(Enumerable.Distinct(__reviewsGiven));
        IReadOnlyList<Review> reviews = [];
        if (__reviewsKeys.Length > 0)
        {
            var __reviewsRows = await repository.FindAsync(
                Spec<Review>.Where(__row => Enumerable.Contains(__reviewsKeys, __row.PersistenceId)),
                ct).ConfigureAwait(false);
            var __reviewsByKey = Enumerable.ToDictionary(__reviewsRows, __row => __row.PersistenceId);
            var __reviewsMissing = Enumerable.ToArray(Enumerable.Where(__reviewsKeys, __key => !__reviewsByKey.ContainsKey(__key)));
            if (__reviewsMissing.Length > 0)
                return Result<IReadOnlyList<Review>, IError>.Failure(NotFoundError.ForAll("Review", __reviewsMissing));
            reviews = Enumerable.ToArray(Enumerable.Select(__reviewsKeys, __key => __reviewsByKey[__key]));
        }

        return Result<IReadOnlyList<Review>, IError>.Success(reviews);
    }

    /// <summary>
    ///     The read of a declared query's invoker: on EF Core <c>IQueryExecutor.ExecuteAsync(q, dbContext.Set&lt;T&gt;())</c>, on the Sql engine the
    ///     boundary's repository, which runs the compiled query (<c>RunAsync</c>). The page is handed back whole, as the template hands it.
    /// </summary>
    public static async Task<Result<PagedResult<Review>, IError>> RunAsync(IReadRepository<Review> repository, SearchReviewsQuery q, CancellationToken cancellationToken = default)
    {
        var answer = await repository.RunAsync(q, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<Review>, IError>.Success(answer);
    }
}
