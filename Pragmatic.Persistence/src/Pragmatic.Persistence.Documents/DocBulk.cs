using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The bulk update of <see cref="IBulkRepository{TEntity}" /> on the documents engine, as the generator intercepts it where it is called: the
///     update compiled from the call (its plan), run on the session of the repository's unit of work with the documents the specification selects
///     and the values the setters' delegate records (<c>BulkSetterValues</c>). MongoDB runs it as one command; Cosmos DB, which writes a document
///     by its id, one document at a time.
/// </summary>
/// <remarks>Set-based, as EF Core's <c>ExecuteUpdate</c>: the unit of work's tracked documents are not refreshed by it.</remarks>
public static class DocBulk
{
    /// <summary>Runs a compiled update: <paramref name="run" /> is the plan, given the session, the predicate and the values.</summary>
    public static async Task<int> UpdateAsync<TEntity>(IBulkRepository<TEntity> repository, ISpecification<TEntity> filter, Action<IBulkSetters<TEntity>> set,
        Func<DocSession, DocPredicate<TEntity>, object?[], CancellationToken, ValueTask<long>> run, CancellationToken ct)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(run);
        var unitOfWork = repository as IDocRepository is { } documents
            ? documents.UnitOfWork
            : throw new InvalidOperationException($"{repository?.GetType().Name ?? "null"} is not a repository of the documents engine: the bulk update compiled for {typeof(TEntity).Name} runs on a documents engine's repository.");
        return checked((int)await run(unitOfWork.Session, DocSpecifications.ToPredicate(filter), BulkSetterValues<TEntity>.Record(set), ct).ConfigureAwait(false));
    }
}
