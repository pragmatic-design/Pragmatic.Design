using Pragmatic.Specification;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Set-based writes over an entity's rows, without loading them: an update and a delete of the rows a specification selects, and an
///     insert and an upsert of many rows at once.
/// </summary>
/// <typeparam name="TEntity">The entity.</typeparam>
/// <remarks>
///     <para>
///         Declared here, where there is no EF Core, so that code written against it does not depend on the persistence. EF Core's
///         generated repository implements it, translating the update to <c>ExecuteUpdate</c>.
///     </para>
///     <para>
///         The entity's rules hold as on any write: the delete of a <c>[SoftDelete]</c> entity marks the rows, the read filters select them.
///     </para>
/// </remarks>
public interface IBulkRepository<TEntity>
    where TEntity : class
{
    /// <summary>Updates the rows <paramref name="filter" /> selects, setting what <paramref name="set" /> names; returns how many.</summary>
    /// <param name="filter">The rows to update.</param>
    /// <param name="set">
    ///     The values, as a chain: <c>s =&gt; s.SetProperty(o =&gt; o.Status, OrderStatus.Closed).SetProperty(o =&gt; o.ClosedAt, now)</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> BulkUpdateAsync(ISpecification<TEntity> filter, Action<IBulkSetters<TEntity>> set, CancellationToken ct = default);

    /// <summary>Deletes the rows <paramref name="filter" /> selects (marks them, on a <c>[SoftDelete]</c> entity); returns how many.</summary>
    Task<int> BulkDeleteAsync(ISpecification<TEntity> filter, CancellationToken ct = default);

    /// <summary>Inserts the rows in batches; returns how many.</summary>
    Task<int> BulkInsertAsync(IReadOnlyList<TEntity> entities, CancellationToken ct);

    /// <summary>Inserts the rows, or updates the ones whose key is there, in batches; returns how many.</summary>
    Task<int> BulkUpsertAsync(IReadOnlyList<TEntity> entities, CancellationToken ct);
}
