using System.Collections.Concurrent;

namespace Pragmatic.Jobs.Stores;

/// <summary>
///     In-memory recurring job store for development and testing.
/// </summary>
public sealed class InMemoryRecurringJobStore : IRecurringJobStore
{
    private readonly ConcurrentDictionary<string, RecurringJobDefinition> _definitions = new();
    // Protects field-level mutations (UpdateNextExecution, Disable, Enable) that are not
    // atomic when racing with each other or with UpsertAsync.
    private readonly Lock _updateLock = new();

    /// <inheritdoc />
    public Task UpsertAsync(RecurringJobDefinition definition, CancellationToken ct = default)
    {
        _definitions[definition.Id] = definition;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecurringJobDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        IReadOnlyList<RecurringJobDefinition> result = _definitions.Values
            .Where(d => d.IsEnabled && d.NextExecutionAt.HasValue && d.NextExecutionAt.Value <= now)
            .OrderBy(d => d.NextExecutionAt)
            .ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task UpdateNextExecutionAsync(string id, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted, CancellationToken ct = default)
    {
        lock (_updateLock)
        {
            if (_definitions.TryGetValue(id, out var def))
            {
                def.LastExecutedAt = lastExecuted;
                def.NextExecutionAt = nextExecution;
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryClaimDueAsync(
        string id,
        DateTimeOffset? expectedNextExecution,
        DateTimeOffset? nextExecution,
        DateTimeOffset lastExecuted,
        CancellationToken ct = default)
    {
        // Compare-and-swap under the update lock: claim succeeds only if NextExecutionAt still
        // matches the observed value, so concurrent scheduler ticks enqueue the job exactly once.
        lock (_updateLock)
        {
            if (!_definitions.TryGetValue(id, out var def) || !def.IsEnabled
                || def.NextExecutionAt != expectedNextExecution)
                return Task.FromResult(false);

            def.LastExecutedAt = lastExecuted;
            def.NextExecutionAt = nextExecution;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<RecurringJobDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        _definitions.TryGetValue(id, out var def);
        return Task.FromResult(def);
    }

    /// <inheritdoc />
    public Task DisableAsync(string id, CancellationToken ct = default)
    {
        lock (_updateLock)
        {
            if (_definitions.TryGetValue(id, out var def))
                def.IsEnabled = false;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnableAsync(string id, CancellationToken ct = default)
    {
        lock (_updateLock)
        {
            if (_definitions.TryGetValue(id, out var def))
                def.IsEnabled = true;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _definitions.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <summary>Total definition count (for testing).</summary>
    public int Count => _definitions.Count;
}
