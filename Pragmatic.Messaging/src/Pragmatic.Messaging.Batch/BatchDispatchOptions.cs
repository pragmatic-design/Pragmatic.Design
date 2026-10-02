namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Tuning for <see cref="BatchDispatcher{TBatch,TItem}"/>.
/// </summary>
public sealed class BatchDispatchOptions
{
    /// <summary>
    ///     Maximum number of items a single <c>DispatchAsync</c> call publishes concurrently.
    ///     Default <c>1</c> — sequential, in-order publishing (unchanged behaviour). Raising it publishes a
    ///     large batch faster while <b>capping</b> how many publishes are in flight at once, so a huge batch
    ///     cannot flood the transport without bound. The dispatcher never exceeds this many concurrent
    ///     publishes. Progress counting is unaffected (it happens consumer-side, per item).
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>Shared default (sequential, in-order).</summary>
    public static BatchDispatchOptions Default { get; } = new();
}
