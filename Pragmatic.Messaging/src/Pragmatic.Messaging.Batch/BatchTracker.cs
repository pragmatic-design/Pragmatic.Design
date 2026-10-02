namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Static helper for handlers to report batch item progress.
///     Reads batch context from <see cref="MessageContext.Headers"/> (set by <see cref="BatchDispatcher{TBatch,TItem}"/>).
/// </summary>
public static class BatchTracker
{
    /// <summary>
    ///     Reports successful processing of a batch item.
    ///     Call this at the end of your handler's <c>HandleAsync</c>.
    /// </summary>
    public static async Task ReportSuccessAsync(MessageContext context, IBatchProgressStore store, CancellationToken ct = default)
    {
        if (TryGetBatchId(context, out var batchId))
            await store.IncrementCompletedAsync(batchId, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reports failed processing of a batch item.
    ///     Call this in your handler's error path.
    /// </summary>
    public static async Task ReportFailureAsync(MessageContext context, IBatchProgressStore store, CancellationToken ct = default)
    {
        if (TryGetBatchId(context, out var batchId))
            await store.IncrementFailedAsync(batchId, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Extracts the batch ID from context headers.
    ///     Returns false if the message is not part of a batch.
    /// </summary>
    public static bool TryGetBatchId(MessageContext context, out Guid batchId)
    {
        batchId = Guid.Empty;
        return context.Headers is not null
            && context.Headers.TryGetValue(BatchHeaders.BatchId, out var idStr)
            && Guid.TryParse(idStr, out batchId);
    }

    /// <summary>
    ///     Gets the batch context (index, total) from message headers.
    ///     Returns null if the message is not part of a batch.
    /// </summary>
    public static BatchContext? GetBatchContext(MessageContext context)
    {
        if (!TryGetBatchId(context, out var batchId))
            return null;

        var index = 0;
        var total = 0;
        if (context.Headers!.TryGetValue(BatchHeaders.ItemIndex, out var indexStr))
            int.TryParse(indexStr, out index);
        if (context.Headers.TryGetValue(BatchHeaders.TotalItems, out var totalStr))
            int.TryParse(totalStr, out total);

        return new BatchContext(batchId, index, total);
    }
}
