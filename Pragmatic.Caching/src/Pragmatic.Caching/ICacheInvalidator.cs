namespace Pragmatic.Caching;

/// <summary>
///     Implemented by an operation — a mutation, an action, an event — to invalidate cache entries
///     after it has committed. The source generator adds this interface to types marked with
///     <c>[InvalidatesCache]</c>.
/// </summary>
/// <remarks>
///     ⚠️ Any type carrying the attribute gets this interface, and every invoker that commits asks for
///     it — an action's invalidation as much as a mutation's. An invalidation that is generated and
///     never called looks correct and keeps a stale page for the whole cache duration.
/// </remarks>
public interface ICacheInvalidator
{
    /// <summary>
    ///     Invalidates relevant cache entries after the operation has completed.
    /// </summary>
    /// <param name="cache">The cache stack to invalidate against.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default);

    /// <summary>
    ///     The target cache category, or <c>null</c> for broadcast invalidation
    ///     across all registered categories.
    /// </summary>
    Type? InvalidationCategory => null;
}
