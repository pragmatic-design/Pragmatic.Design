using Pragmatic.Persistence.Query.Results;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;

namespace PragmaticEntities.Tests;

/// <summary>
///     The read of <c>QueryInvokerTemplate</c> in the form it takes on the Sql engine, written as the template writes it today with the
///     engine's call in place of EF Core's. The validation and the permission around it are the invoker base's and do not change with the
///     engine.
/// </summary>
/// <remarks>
///     TODO: written by hand. The query invoker is emitted with EF Core only (<c>QueryFeature</c> returns without it), so this suite writes
///     its read. It goes when the query invoker is emitted for every engine (step 8 of the persistence engines' port). The loads of
///     <c>LoadEntityTemplate</c> and the mutation invoker's writes are generated and run here (<see cref="GeneratedInvokerTests" />).
/// </remarks>
internal static class ReviewLoads
{
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
