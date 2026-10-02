namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Chooses which rows a navigation points at, from their keys alone.
/// </summary>
/// <typeparam name="TEntity">The aggregate root holding the navigation.</typeparam>
/// <remarks>
///     <para>
///         The sibling of <see cref="INavigationLoader{TEntity}" />, and here for the same reason:
///         attaching a row named only by its key means marking it <c>Unchanged</c> in the change
///         tracker, which needs a <c>DbContext</c>. The caller that needs it — a mutation invoker —
///         must stay free of EF Core, so it asks for a capability rather than for a context.
///     </para>
///     <para>
///         ⚠️ Removing a link is not deleting a row, and which of the two happens is the
///         relationship's business: a many-to-many drops the join row, a one-to-many orphans or
///         cascades according to its configuration. The same division of labour a merged collection
///         of children already has.
///     </para>
/// </remarks>
public interface INavigationLinker<in TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Makes <paramref name="ids" /> the links of <paramref name="navigation" />, per the strategy.
    /// </summary>
    /// <param name="entity">The entity holding the navigation.</param>
    /// <param name="navigation">The navigation's name on the entity.</param>
    /// <typeparam name="TRelated">The entity on the other side of the navigation.</typeparam>
    /// <param name="ids">The keys the caller sent, boxed: they are compared, never typed.</param>
    /// <param name="key">The related entity's key property.</param>
    /// <param name="strategy">
    ///     <c>Sync</c>, <c>AddOnly</c>, <c>Replace</c> or <c>Ignore</c>, spelled as the
    ///     <c>CollectionStrategy</c> member's name. A string, because this contract lives where
    ///     <c>Pragmatic.Mapping</c> is not referenced.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The number of read round trips it took: <c>0</c> when nothing had to be loaded.</returns>
    /// <param name="stubFactory">
    ///     Builds an empty related entity — the generated <c>Create()</c>. Passed in rather than
    ///     constructed here: <c>new TRelated()</c> would exclude every entity with a required member,
    ///     and creating one by reflection is what "no reflection in new code" forbids.
    /// </param>
    Task<int> LinkAsync<TRelated>(
        TEntity entity,
        string navigation,
        IReadOnlyList<object> ids,
        string key,
        string strategy,
        Func<TRelated> stubFactory,
        CancellationToken ct)
        where TRelated : class;
}
