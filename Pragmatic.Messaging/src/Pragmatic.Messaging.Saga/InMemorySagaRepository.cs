using System.Collections.Concurrent;

namespace Pragmatic.Messaging.Saga;

/// <summary>
///     In-memory saga repository for development and testing.
///     Saga state is lost on process restart.
/// </summary>
/// <typeparam name="TSaga">The concrete saga type.</typeparam>
public sealed class InMemorySagaRepository<TSaga>(
    Func<TSaga, Guid> idAccessor,
    Func<TSaga, string> correlationAccessor,
    Func<TSaga, DateTimeOffset?> completedAccessor)
    : ISagaRepository<TSaga>
    where TSaga : class
{
    private readonly ConcurrentDictionary<Guid, TSaga> _sagas = new();
    private readonly ConcurrentDictionary<string, Guid> _correlationIndex = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _timeouts = new();
    // The terminal status the orchestrator ended a saga in (timed out, compensated, faulted). A completed one is read off the saga itself.
    private readonly ConcurrentDictionary<Guid, SagaStatus> _ended = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<string>> _steps = new();

    /// <inheritdoc />
    public Task<TSaga?> FindByCorrelationAsync(string correlationId, CancellationToken ct = default)
    {
        if (_correlationIndex.TryGetValue(correlationId, out var id) && _sagas.TryGetValue(id, out var saga))
            return Task.FromResult<TSaga?>(saga);

        return Task.FromResult<TSaga?>(null);
    }

    /// <inheritdoc />
    public Task<TSaga?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        _sagas.TryGetValue(id, out var saga);
        return Task.FromResult(saga);
    }

    /// <inheritdoc />
    public Task<SagaStatus?> GetStatusAsync(Guid sagaId, CancellationToken ct = default)
    {
        if (!_sagas.TryGetValue(sagaId, out var saga))
            return Task.FromResult<SagaStatus?>(null);
        if (_ended.TryGetValue(sagaId, out var ended))
            return Task.FromResult<SagaStatus?>(ended);
        return Task.FromResult<SagaStatus?>(completedAccessor(saga) is null ? SagaStatus.Active : SagaStatus.Completed);
    }

    /// <inheritdoc />
    public Task SaveAsync(TSaga saga, CancellationToken ct = default)
    {
        var id = idAccessor(saga);
        var correlationId = correlationAccessor(saga);
        // Same guard as the EF repo so a too-long correlation id fails the same way in dev/in-memory
        // as it would against the 128-char column in production.
        SagaInstance.EnsureValidCorrelationId(correlationId);

        _sagas[id] = saga;
        _correlationIndex[correlationId] = id;
        // A save sets the status from the saga again, as the durable stores' saves do: Active, or Completed.
        _ended.TryRemove(id, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveWithTimeoutAsync(TSaga saga, DateTimeOffset? timeoutAt, CancellationToken ct = default)
    {
        var id = idAccessor(saga);
        var correlationId = correlationAccessor(saga);
        SagaInstance.EnsureValidCorrelationId(correlationId);

        // State + deadline updated together (atomic) — mirrors the EF repo's single-commit save.
        _sagas[id] = saga;
        _correlationIndex[correlationId] = id;
        _ended.TryRemove(id, out _);
        if (timeoutAt is null)
            _timeouts.TryRemove(id, out _);
        else
            _timeouts[id] = timeoutAt.Value;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveWithStepAsync(TSaga saga, DateTimeOffset? timeoutAt, string executedStepName, CancellationToken ct = default)
    {
        var id = idAccessor(saga);
        // Record the executed step alongside the state+deadline save.
        _steps.GetOrAdd(id, static _ => new ConcurrentQueue<string>()).Enqueue(executedStepName);
        return SaveWithTimeoutAsync(saga, timeoutAt, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetExecutedStepNamesAsync(Guid sagaId, CancellationToken ct = default)
    {
        IReadOnlyList<string> names = _steps.TryGetValue(sagaId, out var q) ? q.ToList() : [];
        return Task.FromResult(names);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TSaga>> GetActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TSaga> active = _sagas.Values
            .Where(IsActive)
            .ToList();

        return Task.FromResult(active);
    }

    /// <inheritdoc />
    public Task SetTimeoutAsync(Guid sagaId, DateTimeOffset? timeoutAt, CancellationToken ct = default)
    {
        if (timeoutAt is null)
            _timeouts.TryRemove(sagaId, out _);
        else
            _timeouts[sagaId] = timeoutAt.Value;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TSaga>> GetDueForTimeoutAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        IReadOnlyList<TSaga> due = _timeouts
            .Where(kv => kv.Value <= asOf)
            .Select(kv => _sagas.TryGetValue(kv.Key, out var saga) ? saga : null)
            .Where(s => s is not null && IsActive(s))
            .Select(s => s!)
            .ToList();

        return Task.FromResult(due);
    }

    /// <inheritdoc />
    public Task MarkTimedOutAsync(Guid sagaId, CancellationToken ct = default)
    {
        _timeouts.TryRemove(sagaId, out _);
        _ended[sagaId] = SagaStatus.TimedOut;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Kept, as the durable stores keep it, so <see cref="GetStatusAsync" /> tells a compensated saga from a completed one.</remarks>
    public Task MarkCompensatedAsync(Guid sagaId, CancellationToken ct = default)
    {
        _timeouts.TryRemove(sagaId, out _);
        _ended[sagaId] = SagaStatus.Compensated;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFaultedAsync(Guid sagaId, string error, CancellationToken ct = default)
    {
        _ended[sagaId] = SagaStatus.Faulted;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Not completed and not ended by the orchestrator (timed out, compensated, faulted): what the durable stores read as
    ///     <c>Status == Active</c> (#909).
    /// </summary>
    private bool IsActive(TSaga saga) => completedAccessor(saga) is null && !_ended.ContainsKey(idAccessor(saga));

    /// <summary>Total saga count.</summary>
    public int Count => _sagas.Count;

    /// <summary>True if the saga has been marked timed-out. Exposed for tests / diagnostics.</summary>
    public bool IsTimedOut(Guid sagaId) => _ended.TryGetValue(sagaId, out var status) && status == SagaStatus.TimedOut;
}
