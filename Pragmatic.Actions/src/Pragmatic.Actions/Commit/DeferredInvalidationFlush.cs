using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Actions.Commit;

/// <summary>
///     Runs the cache invalidations that nested invocations deferred, once the owner has committed.
/// </summary>
/// <remarks>
///     <para>
///         An operation that does not own its commit cannot invalidate when it returns: nothing it wrote is committed yet, and a read
///         in between would put the old state back in the entry, where it outlives the commit. A nested mutation used to drop its
///         invalidation altogether, so a mutation used as a step never invalidated its entries; a nested action ran its own before the
///         owner saved (#707).
///     </para>
///     <para>
///         Called inside the owner's post-commit block, after <see cref="DeferredEventFlush" />: like the event dispatch, a cache that
///         refuses must not turn a committed operation into a reported failure.
///     </para>
/// </remarks>
public static class DeferredInvalidationFlush
{
    /// <summary>Runs everything deferred into <paramref name="batch" />, in order, if anything was.</summary>
    /// <param name="batch">The owner's batch, or <c>null</c> when this invocation owned nothing.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task RunAsync(BatchContext? batch, CancellationToken ct)
    {
        if (batch is null)
            return;

        foreach (var invalidate in batch.DeferredInvalidations)
            await invalidate(ct).ConfigureAwait(false);
    }
}
