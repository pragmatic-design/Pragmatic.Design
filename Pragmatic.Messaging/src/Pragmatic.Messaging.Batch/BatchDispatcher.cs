using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Orchestrates batch processing: split → publish → track.
///     Resolves <see cref="IBatchSplitter{TBatch,TItem}"/>, creates progress entry,
///     and publishes each item via <see cref="IMessageBus"/> with batch headers.
/// </summary>
/// <typeparam name="TBatch">The batch command type.</typeparam>
/// <typeparam name="TItem">The individual item type published to the bus.</typeparam>
public sealed partial class BatchDispatcher<TBatch, TItem>(
    IBatchSplitter<TBatch, TItem> splitter,
    IBatchProgressStore progressStore,
    IMessageBus messageBus,
    ILogger<BatchDispatcher<TBatch, TItem>> logger,
    BatchDispatchOptions? options = null) : IBatchDispatcher<TBatch, TItem>
    where TItem : notnull
{
    private readonly BatchDispatchOptions _options = options ?? BatchDispatchOptions.Default;

    /// <summary>
    ///     Splits the batch, publishes each item to the message bus, and creates a progress entry.
    ///     Returns the batch ID for progress tracking.
    /// </summary>
    /// <param name="batch">The batch command to split and dispatch.</param>
    /// <param name="label">Optional descriptive label for monitoring.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unique batch ID.</returns>
    public Task<Guid> DispatchAsync(TBatch batch, string? label = null, CancellationToken ct = default)
        => DispatchInternalAsync(batch, label, resumeBatchId: null, ct);

    /// <summary>
    ///     Resumes a batch whose dispatch was interrupted (e.g. a crash mid-loop): re-splits the
    ///     batch and publishes only the items after the persisted <see cref="BatchProgress.DispatchedCount"/>
    ///     checkpoint, instead of re-publishing everything or leaving the batch orphaned.
    /// </summary>
    /// <remarks>
    ///     The caller MUST pass the SAME <paramref name="batch"/> so <c>Split</c> yields the same items in the
    ///     same order (the split must be deterministic). Fine-grained resume applies only to the sequential
    ///     path (<see cref="BatchDispatchOptions.MaxConcurrency"/> = 1); a parallel batch has no per-item
    ///     checkpoint and re-publishes from the start, relying on batch-count idempotency and
    ///     consumer item idempotency to avoid double effects.
    /// </remarks>
    /// <param name="batch">The original batch command (must split deterministically to the same items).</param>
    /// <param name="batchId">The id returned by the original <see cref="DispatchAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<Guid> ResumeAsync(TBatch batch, Guid batchId, CancellationToken ct = default)
        => DispatchInternalAsync(batch, label: null, resumeBatchId: batchId, ct);

    private async Task<Guid> DispatchInternalAsync(TBatch batch, string? label, Guid? resumeBatchId, CancellationToken ct)
    {
        var items = splitter.Split(batch);
        var total = items.Count;

        Guid batchId;
        var startIndex = 0;
        var existing = resumeBatchId is { } rid
            ? await progressStore.GetProgressAsync(rid, ct).ConfigureAwait(false)
            : null;

        if (existing is not null)
        {
            // Resume: continue after the checkpoint (sequential path). Bounded so a stale/rerun
            // checkpoint can never index past the item list.
            batchId = existing.BatchId;
            startIndex = Math.Clamp(existing.DispatchedCount, 0, total);
            LogBatchResuming(batchId, startIndex, total);
        }
        else
        {
            batchId = resumeBatchId ?? Guid.NewGuid();
            var progress = new BatchProgress
            {
                BatchId = batchId,
                Total = total,
                StartedAt = DateTimeOffset.UtcNow,
                Label = label,
            };
            await progressStore.CreateAsync(progress, ct).ConfigureAwait(false);
            LogBatchDispatching(batchId, total, label);
        }

        Task PublishItemAsync(int index, CancellationToken itemCt)
        {
            var context = MessageContext.New() with
            {
                Headers = new Dictionary<string, string>
                {
                    [BatchHeaders.BatchId] = batchId.ToString(),
                    [BatchHeaders.ItemIndex] = index.ToString(),
                    [BatchHeaders.TotalItems] = total.ToString(),
                }
            };

            return messageBus.PublishAsync(items[index], context, itemCt);
        }

        var maxConcurrency = Math.Max(1, _options.MaxConcurrency);
        if (maxConcurrency == 1)
        {
            // Default: sequential, in-order (one publish in flight). Checkpoint after each publish so a
            // crash resumes from exactly where it stopped.
            for (var i = startIndex; i < total; i++)
            {
                await PublishItemAsync(i, ct).ConfigureAwait(false);
                await progressStore.SetDispatchedCountAsync(batchId, i + 1, ct).ConfigureAwait(false);
            }
        }
        else
        {
            // Bounded parallel: never more than MaxConcurrency publishes in flight, so a large batch cannot
            // flood the transport. Out-of-order publishes can't be captured by a single counter, so there is
            // no fine-grained resume here — the checkpoint is only stamped once all items are published.
            await Parallel.ForEachAsync(
                Enumerable.Range(startIndex, total - startIndex),
                new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct },
                async (i, itemCt) => await PublishItemAsync(i, itemCt).ConfigureAwait(false))
                .ConfigureAwait(false);
            await progressStore.SetDispatchedCountAsync(batchId, total, ct).ConfigureAwait(false);
        }

        LogBatchDispatched(batchId, total);
        return batchId;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Dispatching batch {BatchId}: {ItemCount} items, label={Label}")]
    partial void LogBatchDispatching(Guid batchId, int itemCount, string? label);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Resuming batch {BatchId} from item {StartIndex} of {ItemCount}")]
    partial void LogBatchResuming(Guid batchId, int startIndex, int itemCount);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Batch {BatchId} dispatched: {ItemCount} items published")]
    partial void LogBatchDispatched(Guid batchId, int itemCount);
}
