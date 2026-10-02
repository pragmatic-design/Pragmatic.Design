using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Reports batch item progress automatically for any message carrying the batch headers, so a
///     batch completes without the handler having to remember to call <see cref="BatchTracker"/>.
/// </summary>
/// <remarks>
///     <para>
///     Before this existed, progress advanced only when a handler explicitly reported. A handler that
///     threw (or whose message was dead-lettered by the pipeline) never incremented <c>Failed</c>, so
///     <c>Pending</c> never reached zero and the batch stayed "active" forever — the completion of the
///     batch depended on handler discipline rather than on what actually happened.
///     </para>
///     <para>
///     Ordered <c>int.MinValue</c> so it is the OUTERMOST middleware: it observes the outcome after
///     retry/circuit-breaker have run, i.e. the message's final fate, not an intermediate attempt.
///     Failures are reported and then rethrown — reporting never swallows the error.
///     </para>
///     <para>
///     With this registered, handlers must NOT also call <see cref="BatchTracker.ReportSuccessAsync"/>
///     or <see cref="BatchTracker.ReportFailureAsync"/>: the item would be counted twice. Those helpers
///     remain for topologies that opt out of the middleware and report by hand. A handler that processed
///     its item but must fail it — part of its work refused — calls <see cref="BatchItemOutcome.Fail"/>
///     and returns: counted as failed, without the retry and rollback a throw would bring.
///     </para>
///     <para>
///     Counting is deduplicated per <c>(batchId, itemIndex)</c> when an <see cref="IIdempotencyStore"/>
///     is registered: each item is counted at most once (first final outcome wins), so an
///     at-least-once redelivery — even one re-published with a fresh MessageId that consumer idempotency
///     would not catch — cannot inflate the counters past <c>Total</c>. A fail-then-succeed redelivery is
///     counted by its FIRST outcome (a documented trade-off, not inflation). Without an idempotency store
///     there is nothing to remember, so counting falls back to at-least-once.
///     </para>
/// </remarks>
public sealed partial class BatchProgressMiddleware(
    IBatchProgressStore store,
    ILogger<BatchProgressMiddleware> logger,
    IIdempotencyStore? idempotencyStore = null,
    BatchItemOutcome? outcome = null) : IMessageMiddleware
{
    /// <inheritdoc />
    public int Order => int.MinValue;

    /// <inheritdoc />
    public async Task InvokeAsync<T>(T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
        where T : notnull
    {
        if (!BatchTracker.TryGetBatchId(context, out var batchId))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Trust model: the batch.id header is only a hint — the store is authoritative. If no batch with
        // this id exists, ignore the header (don't count against a phantom batch) instead of relying on
        // each store's silent no-op. A message forging the id of a REAL other batch still can't be
        // distinguished here without an authenticated transport / signed per-item token — documented on
        // BatchHeaders as a residual.
        if (await store.GetProgressAsync(batchId, ct).ConfigureAwait(false) is null)
        {
            LogUnknownBatch(batchId);
            await next().ConfigureAwait(false);
            return;
        }

        var itemIndex = BatchTracker.GetBatchContext(context)?.ItemIndex;

        try
        {
            await next().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Report the failure, then let it propagate: the batch learns the item's fate even though
            // the message still goes on to be retried/dead-lettered by the pipeline.
            if (await ShouldCountAsync(batchId, itemIndex, ct).ConfigureAwait(false))
                await ReportAsync(() => store.IncrementFailedAsync(batchId, ct), batchId, ct).ConfigureAwait(false);
            throw;
        }

        // The handler returned. It may still have failed the item on purpose (BatchItemOutcome): processed,
        // with part of its work refused — counted as failed, without the retry a throw would cause.
        if (await ShouldCountAsync(batchId, itemIndex, ct).ConfigureAwait(false))
        {
            if (outcome is { Failed: true })
                await ReportAsync(() => store.IncrementFailedAsync(batchId, ct), batchId, ct).ConfigureAwait(false);
            else
                await ReportAsync(() => store.IncrementCompletedAsync(batchId, ct), batchId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Claims the count for a <c>(batchId, itemIndex)</c> so an at-least-once redelivery cannot count the
    ///     same item twice. Returns true the FIRST time an item's outcome is observed, false afterwards.
    ///     Without an idempotency store (nothing to remember) or an item index, counting is at-least-once.
    /// </summary>
    private async Task<bool> ShouldCountAsync(Guid batchId, int? itemIndex, CancellationToken ct)
    {
        if (idempotencyStore is null || itemIndex is null)
            return true;

        return await idempotencyStore
            .TryMarkAsProcessedAsync($"batch-count:{batchId:N}:{itemIndex}", ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Progress bookkeeping must never break message processing: a store failure is logged and
    ///     swallowed (the batch may under-count) rather than failing an item that actually succeeded.
    /// </summary>
    private async Task ReportAsync(Func<Task> report, Guid batchId, CancellationToken ct)
    {
        try
        {
            await report().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReportFailed(batchId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Batch progress report failed for batch {BatchId} — the batch may under-count")]
    private partial void LogReportFailed(Guid batchId, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Ignoring batch header {BatchId}: no such batch in the store (unknown or forged id)")]
    private partial void LogUnknownBatch(Guid batchId);
}
