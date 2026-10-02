using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Specification;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Read-only repository interface for querying entities.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IReadRepository<TEntity>
    where TEntity : class, IEntity
{
    /// <summary>
    ///     Gets an entity by its primary key.
    /// </summary>
    /// <param name="id">The entity's primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entity if found; otherwise, null.</returns>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    ///     Finds entities matching a specification.
    /// </summary>
    /// <param name="spec">The specification to match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching entities.</returns>
    Task<List<TEntity>> FindAsync(ISpecification<TEntity> spec, CancellationToken ct = default);

    /// <summary>
    ///     Counts entities matching a specification.
    /// </summary>
    /// <param name="spec">The specification to match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The count of matching entities.</returns>
    Task<int> CountAsync(ISpecification<TEntity> spec, CancellationToken ct = default);

    /// <summary>
    ///     Checks if any entity matches a specification.
    /// </summary>
    /// <param name="spec">The specification to match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if any entity matches; otherwise, false.</returns>
    Task<bool> ExistsAsync(ISpecification<TEntity> spec, CancellationToken ct = default);

    /// <summary>
    ///     Gets the first entity matching a specification, or null if none.
    /// </summary>
    /// <param name="spec">The specification to match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first matching entity or null.</returns>
    Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> spec, CancellationToken ct = default);

    /// <summary>
    ///     Gets a queryable for advanced query scenarios that cannot be expressed via
    ///     <see cref="FindAsync"/> and specifications.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> for the entity type.</returns>
    /// <remarks>
    ///     <b>Use with caution.</b> This method leaks the underlying EF Core query provider
    ///     through the repository abstraction, coupling callers to the persistence technology.
    ///     Prefer <see cref="FindAsync"/> with a typed <c>ISpecification</c> for all standard
    ///     query scenarios. Reserve <c>Query()</c> for advanced cases (projections, joins,
    ///     raw SQL) where a specification would be impractical, and keep such callers in the
    ///     infrastructure layer rather than the domain.
    /// </remarks>
    IQueryable<TEntity> Query();

    /// <summary>
    ///     Runs a declared <c>[Query]</c> against this repository's own set, and answers its rows.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>No operation pipeline runs here.</b> This sits at the same level as
    ///         <see cref="Query" />: whoever calls it is inside a <c>[DomainAction]</c> that already has
    ///         its validation, its permission and its transaction from its own invoker. The path
    ///         <i>with</i> the pipeline is the query invoker, reached by name through the boundary
    ///         facade — <c>catalog.CatalogItems.SearchCatalogItems(…)</c>. Choose this one to reuse a
    ///         declared read inside an operation; choose the facade to invoke an operation.
    ///     </para>
    ///     <para>
    ///         The <c>DbContext</c> never appears in the caller's code, which is the point: without this,
    ///         reusing a declared query means resolving <c>IQueryExecutor</c>, obtaining a raw entity
    ///         set and picking an overload — enough friction that applications write the LINQ again by
    ///         hand.
    ///     </para>
    /// </remarks>
    Task<IReadOnlyList<TResult>> RunAsync<TResult>(
        IQuery<TEntity, TResult> query,
        CancellationToken ct = default)
        where TResult : class;

    /// <summary>
    ///     Runs a declared paged <c>[Query]</c> against this repository's own set.
    /// </summary>
    /// <inheritdoc cref="RunAsync{TResult}(IQuery{TEntity, TResult}, CancellationToken)" path="/remarks" />
    Task<PagedResult<TResult>> RunAsync<TResult>(
        IPagedQuery<TEntity, TResult> query,
        CancellationToken ct = default)
        where TResult : class;
}
