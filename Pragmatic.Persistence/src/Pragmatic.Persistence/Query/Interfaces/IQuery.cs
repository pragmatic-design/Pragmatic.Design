using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Interfaces;

/// <summary>
///     Marker interface for query objects.
///     Queries apply filtering, sorting, and optionally paging to a data source.
/// </summary>
/// <typeparam name="TEntity">The source entity type.</typeparam>
public interface IQuery<TEntity> where TEntity : class
{
    /// <summary>
    ///     Applies query logic (filtering, sorting) to the queryable.
    ///     Does NOT execute the query - returns an IQueryable.
    /// </summary>
    /// <param name="query">The source queryable.</param>
    /// <returns>The transformed queryable (not yet executed).</returns>
    IQueryable<TEntity> Apply(IQueryable<TEntity> query);
}

/// <summary>
///     Query with projection to a result type.
/// </summary>
/// <typeparam name="TEntity">The source entity type.</typeparam>
/// <typeparam name="TResult">The projected result type (DTO).</typeparam>
/// <remarks>
///     <para>
///         The <see cref="Projection"/> property is source-generated based on:
///         <list type="bullet">
///             <item>Mapping attributes on TResult (e.g., [MapFrom&lt;TEntity&gt;])</item>
///             <item>Convention-based property matching</item>
///         </list>
///     </para>
/// </remarks>
public interface IQuery<TEntity, TResult> : IQuery<TEntity>
    where TEntity : class
    where TResult : class
{
    /// <summary>
    ///     The projection expression from entity to result type.
    /// </summary>
    /// <remarks>
    ///     Source-generated from mapping attributes on TResult.
    ///     Used for Select() in EF queries.
    ///     Returns null if no projection is defined (entities are returned as-is).
    /// </remarks>
    Expression<Func<TEntity, TResult>>? Projection => null;

    /// <summary>
    ///     Maps each row after it arrives, for a result type that does not project into SQL.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Null unless the query declares <c>MapInMemory = true</c>. The two are exclusive and
    ///     the executor prefers <see cref="Projection" />: a query that offered both would be
    ///     answering the same question twice, and the answers could differ.
    ///     <para>
    ///     Only the projection moves. Everything that narrows the set — the filters, the sort, the
    ///     page — is applied to the queryable first, so this maps the rows that were coming back
    ///     anyway rather than a table.
    ///     </para>
    /// </remarks>
    Func<TEntity, TResult>? MapEach => null;

    /// <summary>
    ///     Turns the filtered set into the projected set in one step, for a read whose rows are not
    ///     rows of the entity.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A grouping cannot travel as <see cref="Projection" />: that is one entity in and one
    ///     result out, and after a <c>GROUP BY</c> the source is groups, not entities. So an aggregate
    ///     read hands over the whole step — which is the shape a <c>[QueryView]</c>'s generated
    ///     <c>Build</c> already has.
    ///     <para>
    ///         Null unless the result type declares the grouping. The executor prefers it over both
    ///         <see cref="Projection" /> and <see cref="MapEach" />, because a query that offered two
    ///         would be answering the same question twice.
    ///     </para>
    ///     <para>
    ///         It stays in SQL: the returned queryable is composed on, counted and paged by the
    ///         executor, never materialised first.
    ///     </para>
    /// </remarks>
    Func<IQueryable<TEntity>, IQueryable<TResult>>? Aggregate => null;
}
