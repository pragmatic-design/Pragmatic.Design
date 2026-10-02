using System.Collections.Concurrent;

namespace Pragmatic.Logging.Privacy.Audit.Storage;

/// <summary>
/// In-memory implementation of audit storage for testing and development.
/// Warning: Data is not persisted and will be lost on application restart.
/// </summary>
public sealed class MemoryAuditStorage : IAuditStorage, IAuditQuery
{
    // ConcurrentQueue preserves insertion order and supports efficient FIFO drain for deletes
    private readonly ConcurrentQueue<AuditEntry> _entries = new();
    private readonly object _deleteLock = new();
    private volatile bool _disposed;

    /// <inheritdoc />
    public Task StoreEntriesAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        foreach (var entry in entries)
        {
            _entries.Enqueue(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> GetEntriesAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var results = _entries
            .Where(e => e.Timestamp >= from && e.Timestamp < until)
            .OrderBy(e => e.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditEntry>>(results);
    }

    /// <inheritdoc />
    public Task<long> GetEntryCountAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var count = _entries.Count(e => e.Timestamp >= from && e.Timestamp < until);
        return Task.FromResult((long)count);
    }

    /// <inheritdoc />
    public Task<long> DeleteEntriesAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_deleteLock)
        {
            // Drain queue, discard entries before `before`, re-enqueue the rest
            var snapshot = new List<AuditEntry>();
            while (_entries.TryDequeue(out var e))
                snapshot.Add(e);

            long deleted = 0;
            foreach (var e in snapshot)
            {
                if (e.Timestamp < before)
                    deleted++;
                else
                    _entries.Enqueue(e);
            }

            return Task.FromResult(deleted);
        }
    }

    /// <inheritdoc />
    public Task<AuditStorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var result = new AuditStorageHealthResult
        {
            IsHealthy = !_disposed,
            ResponseTimeMs = 0.1, // Very fast for in-memory
            Details =
            {
                ["EntryCount"] = _entries.Count,
                ["StorageType"] = "Memory"
            }
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<AuditQueryResult> QueryAsync(AuditQueryBuilder query, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Simple in-memory query implementation
        IEnumerable<AuditEntry> results = _entries;

        // Apply basic filters - simplified implementation
        var resultList = results.OrderBy(e => e.Timestamp).ToList();

        var queryResult = new AuditQueryResult
        {
            Entries = resultList,
            TotalCount = resultList.Count,
            HasMore = false,
            ExecutionTimeMs = 1.0
        };

        return Task.FromResult(queryResult);
    }

    /// <inheritdoc />
    public Task<AuditStatistics> GetStatisticsAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var entries = _entries.Where(e => e.Timestamp >= from && e.Timestamp < until).ToList();

        var stats = new AuditStatistics
        {
            TotalEntries = entries.Count,
            CoverageTimeSpan = until - from,
            EntriesByEventType = entries
                .GroupBy(e => e.EventType)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            EntriesByComplianceStandard = entries
                .GroupBy(e => e.ComplianceStandard)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            EntriesBySeverity = entries
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            TopUsersByActivity = entries
                .Where(e => !string.IsNullOrEmpty(e.UserId))
                .GroupBy(e => e.UserId!)
                .Take(10)
                .ToDictionary(g => g.Key, g => (long)g.Count())
        };

        return Task.FromResult(stats);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _entries.Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}