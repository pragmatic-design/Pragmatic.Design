using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The bulk writes of <see cref="IBulkRepository{TEntity}" /> on the Sql engine, as the generator intercepts them where they are called: the
///     statement compiled from the call (its plan), run on the session of the repository's unit of work with the rows the specification selects
///     and, for an update, the values the setters' delegate records (<c>BulkSetterValues</c>).
/// </summary>
/// <remarks>Set-based, as EF Core's <c>ExecuteUpdate</c>: the unit of work's tracked rows are not refreshed by it.</remarks>
public static class SqlBulk
{
    /// <summary>Runs a compiled update: <paramref name="run" /> is the plan, given the session, the predicate and the values in their slots.</summary>
    public static async Task<int> UpdateAsync<TEntity>(IBulkRepository<TEntity> repository, ISpecification<TEntity> filter, Action<IBulkSetters<TEntity>> set,
        Func<SqlSession, SqlPredicate<TEntity>, object?[], CancellationToken, ValueTask<int>> run, CancellationToken ct)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(run);
        var unitOfWork = UnitOfWorkOf(repository);
        return await run(unitOfWork.Session, SqlSpecifications.ToPredicate(filter), BulkSetterValues<TEntity>.Record(set), ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs a compiled delete: <paramref name="run" /> is the plan, given the session, the predicate and, for a <c>[SoftDelete]</c> entity's
    ///     marking, the instant and the caller (the unit of work's), in that order.
    /// </summary>
    public static async Task<int> DeleteAsync<TEntity>(IBulkRepository<TEntity> repository, ISpecification<TEntity> filter,
        Func<SqlSession, SqlPredicate<TEntity>, object?[], CancellationToken, ValueTask<int>> run, CancellationToken ct)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(run);
        var unitOfWork = UnitOfWorkOf(repository);
        var (at, by) = unitOfWork.DeletionStamp();
        return await run(unitOfWork.Session, SqlSpecifications.ToPredicate(filter), [at, by], ct).ConfigureAwait(false);
    }

    private static SqlUnitOfWork UnitOfWorkOf<TEntity>(IBulkRepository<TEntity> repository) where TEntity : class
        => repository as ISqlRepository is { } sql
            ? sql.UnitOfWork
            : throw new InvalidOperationException($"{repository?.GetType().Name ?? "null"} is not a repository of the Sql engine: the bulk write compiled for {typeof(TEntity).Name} runs on a Sql engine's repository.");
}
