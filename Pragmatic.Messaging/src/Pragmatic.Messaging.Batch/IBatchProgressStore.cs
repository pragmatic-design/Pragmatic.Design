namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Persists and queries batch progress.
/// </summary>
public interface IBatchProgressStore
{
    /// <summary>Creates a new batch tracking entry.</summary>
    Task CreateAsync(BatchProgress progress, CancellationToken ct = default);

    /// <summary>Increments the completed count for a batch.</summary>
    Task IncrementCompletedAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Increments the failed count for a batch.</summary>
    Task IncrementFailedAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Gets the current progress of a batch.</summary>
    Task<BatchProgress?> GetProgressAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>
    ///     Records how many items the dispatcher has published so far (the crash-resume checkpoint,
    ///     <see cref="BatchProgress.DispatchedCount"/>). Idempotent set (last write wins).
    /// </summary>
    Task SetDispatchedCountAsync(Guid batchId, int dispatchedCount, CancellationToken ct = default);

    /// <summary>
    ///     Gets all active batches. Canonical definition of "active": <see cref="BatchProgress.CompletedAt"/>
    ///     is null. A store must stamp <c>CompletedAt</c> when a batch completes — including a zero-item batch
    ///     at creation, which is complete immediately — so both the in-memory and EF stores agree (EF cannot
    ///     translate the computed <see cref="BatchProgress.IsComplete"/> to SQL, hence <c>CompletedAt</c> is
    ///     the persisted signal).
    /// </summary>
    Task<IReadOnlyList<BatchProgress>> GetActiveAsync(CancellationToken ct = default);
}
