namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Tops up an entity the caller already had, loading only the navigation paths it is missing.
/// </summary>
/// <typeparam name="TEntity">The aggregate root.</typeparam>
/// <remarks>
///     <para>
///         Declared here, where there is no EF Core, and implemented by the generated repository,
///         where the <c>DbContext</c> is. The caller that needs it — a mutation invoker handed an
///         entity by a domain action — must stay free of EF too, so it asks for a capability rather
///         than for a context.
///     </para>
///     <para>
///         ⚠️ The contract is «only what is missing». An implementation that reloads everything is
///         a round trip for rows already in hand; one that assumes the graph is complete removes
///         nothing on a merge and adds everything, which is silent data loss. Both are wrong, and the
///         difference between them is a question to the change tracker, which costs no query.
///     </para>
/// </remarks>
public interface INavigationLoader<in TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Ensures every path in <paramref name="paths" /> is loaded on <paramref name="entity" />.
    /// </summary>
    /// <param name="entity">The entity handed over by the caller.</param>
    /// <param name="paths">Dotted navigation paths, e.g. <c>Lines.Allocations</c>.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    ///     The number of read round trips it took: <c>0</c> when the graph was already complete,
    ///     <c>1</c> otherwise. Returned so a caller — or a test — can assert the cost instead of
    ///     assuming it.
    /// </returns>
    Task<int> EnsureLoadedAsync(TEntity entity, IReadOnlyList<string> paths, CancellationToken ct);
}
