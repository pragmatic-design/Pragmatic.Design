namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Well-known header keys for batch processing context.
///     Set by <see cref="BatchDispatcher{TBatch,TItem}"/> and read by <see cref="BatchTracker"/>.
/// </summary>
/// <remarks>
///     Trust model: these headers are a <b>hint</b>, not a credential. Progress is only ever counted
///     against a batch that actually exists in the <see cref="IBatchProgressStore"/> (checked by
///     <see cref="BatchProgressMiddleware"/>), so an unknown/forged <see cref="BatchId"/> is ignored.
///     A publisher that can forge the id of a <i>real</i> other batch could still skew that batch's
///     counters — defending against that needs an authenticated transport (only trusted producers can
///     publish) or a signed per-item token, which this layer does not provide.
/// </remarks>
public static class BatchHeaders
{
    public const string BatchId = "batch.id";
    public const string ItemIndex = "batch.index";
    public const string TotalItems = "batch.total";
}
