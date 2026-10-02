using System.Collections.Concurrent;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     In-memory batch progress store for development and testing.
/// </summary>
public sealed class InMemoryBatchProgressStore : IBatchProgressStore
{
    private readonly ConcurrentDictionary<Guid, BatchProgress> _batches = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task CreateAsync(BatchProgress progress, CancellationToken ct = default)
    {
        // A batch with zero items is already complete at creation — stamp CompletedAt so it agrees with
        // the canonical "active = CompletedAt is null" signal (otherwise it would stay active forever).
        if (progress.IsComplete && progress.CompletedAt is null)
            progress.CompletedAt = DateTimeOffset.UtcNow;

        _batches[progress.BatchId] = progress;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task IncrementCompletedAsync(Guid batchId, CancellationToken ct = default)
    {
        if (_batches.TryGetValue(batchId, out var progress))
        {
            lock (_lock)
            {
                progress.Completed++;
                CheckCompletion(progress);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task IncrementFailedAsync(Guid batchId, CancellationToken ct = default)
    {
        if (_batches.TryGetValue(batchId, out var progress))
        {
            lock (_lock)
            {
                progress.Failed++;
                CheckCompletion(progress);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BatchProgress?> GetProgressAsync(Guid batchId, CancellationToken ct = default)
    {
        _batches.TryGetValue(batchId, out var progress);
        return Task.FromResult(progress);
    }

    /// <inheritdoc />
    public Task SetDispatchedCountAsync(Guid batchId, int dispatchedCount, CancellationToken ct = default)
    {
        if (_batches.TryGetValue(batchId, out var progress))
        {
            lock (_lock)
                progress.DispatchedCount = dispatchedCount;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BatchProgress>> GetActiveAsync(CancellationToken ct = default)
    {
        // Canonical "active" = CompletedAt is null — the same predicate the EF store uses (IsComplete is a
        // computed property EF cannot translate to SQL), so both stores agree on what "active" means.
        IReadOnlyList<BatchProgress> active = _batches.Values
            .Where(b => b.CompletedAt is null)
            .ToList();
        return Task.FromResult(active);
    }

    /// <summary>Total tracked batches.</summary>
    public int Count => _batches.Count;

    private static void CheckCompletion(BatchProgress progress)
    {
        if (progress.IsComplete && progress.CompletedAt is null)
            progress.CompletedAt = DateTimeOffset.UtcNow;
    }
}
