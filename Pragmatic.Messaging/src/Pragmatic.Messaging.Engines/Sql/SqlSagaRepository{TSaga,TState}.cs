using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Saga;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Engines.Sql;

/// <summary>
///     The saga repository of an <c>[EnableSagaPersistence]</c> boundary the Sql engine serves, over the boundary's session: what
///     <c>EfCoreSagaRepository</c> does, on the same tables. Each save writes the instance, the executed step and, when the boundary is
///     <c>[EnableOutbox]</c> (<paramref name="outbox" />), the step's messages in one transaction; the instance is updated only at the version its
///     state was read with, so a writer that read it before another's save loses (<see cref="SagaConcurrencyException" />).
/// </summary>
/// <remarks>
///     The instance is not a Pragmatic entity, so no session filter reads its tenant. In an application with a tenant context the reads by id and
///     by correlation filter on the ambient tenant themselves, failing closed without one, as EF Core's tenant filter does there; an application
///     without one (single-tenant: EF Core emits no filter) reads them unfiltered, which also holds on Oracle, where the empty tenant is NULL.
///     The background reads (active sagas, due timeouts) and the marks are cross-tenant, as on EF Core.
/// </remarks>
public sealed partial class SqlSagaRepository<TSaga, TState>(
    SqlSession session,
    ILogger<SqlSagaRepository<TSaga, TState>> logger,
    PragmaticJsonOptions? jsonOptions = null,
    ITenantContext? tenantContext = null,
    bool outbox = false,
    TimeProvider? time = null) : ISagaRepository<TSaga>
    where TSaga : class, ISaga<TState>, new()
    where TState : struct, Enum
{
    private static readonly string SagaType = SagaRecords<TSaga, TState>.SagaType;
    private readonly SagaRecords<TSaga, TState> _records = new(jsonOptions);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    // The version each saga's state was read with: the save compares against it, not against a version read at save time.
    private readonly Dictionary<Guid, int> _readVersions = [];

    private string Tenant => tenantContext?.TenantId ?? "";

    /// <inheritdoc />
    public async Task<TSaga?> FindByCorrelationAsync(string correlationId, CancellationToken ct = default)
    {
        var row = tenantContext is null
            ? await session.Query<SagaInstanceRow>()
                .FirstOrDefaultAsync((r, a) => r.SagaType == a.Type && r.CorrelationId == a.Correlation, (Type: SagaType, Correlation: correlationId), ct).ConfigureAwait(false)
            : await session.Query<SagaInstanceRow>()
                .FirstOrDefaultAsync((r, a) => r.SagaType == a.Type && r.CorrelationId == a.Correlation && r.TenantId == a.Tenant,
                    (Type: SagaType, Correlation: correlationId, Tenant: Tenant), ct).ConfigureAwait(false);
        return row is null ? null : Read(row);
    }

    /// <inheritdoc />
    public async Task<TSaga?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = tenantContext is null
            ? await session.Query<SagaInstanceRow>().FirstOrDefaultAsync((r, a) => r.Id == a.Id && r.SagaType == a.Type, (Id: id, Type: SagaType), ct).ConfigureAwait(false)
            : await session.Query<SagaInstanceRow>()
                .FirstOrDefaultAsync((r, a) => r.Id == a.Id && r.SagaType == a.Type && r.TenantId == a.Tenant, (Id: id, Type: SagaType, Tenant: Tenant), ct)
                .ConfigureAwait(false);
        return row is null ? null : Read(row);
    }

    /// <inheritdoc />
    public Task SaveAsync(TSaga saga, CancellationToken ct = default) => SaveCoreAsync(saga, null, applyTimeout: false, null, null, ct);

    /// <inheritdoc />
    public Task SaveWithTimeoutAsync(TSaga saga, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => SaveCoreAsync(saga, timeoutAt, applyTimeout: true, null, null, ct);

    /// <inheritdoc />
    public Task SaveWithStepAsync(TSaga saga, DateTimeOffset? timeoutAt, string executedStepName, CancellationToken ct = default)
        => SaveCoreAsync(saga, timeoutAt, applyTimeout: true, executedStepName, null, ct);

    /// <inheritdoc />
    public async Task<bool> SaveWithStepAndOutboxAsync(TSaga saga, DateTimeOffset? timeoutAt, string executedStepName, IReadOnlyList<object> pendingMessages,
        CancellationToken ct = default)
    {
        // Through the outbox only when the boundary has one ([EnableOutbox]); otherwise the orchestrator publishes inline.
        if (pendingMessages.Count == 0 || !outbox)
        {
            await SaveWithStepAsync(saga, timeoutAt, executedStepName, ct).ConfigureAwait(false);
            return pendingMessages.Count == 0;
        }

        var messages = _records.Outbox(pendingMessages, saga.CorrelationId, tenantContext?.TenantId, _time.GetUtcNow());
        await SaveCoreAsync(saga, timeoutAt, applyTimeout: true, executedStepName, messages, ct).ConfigureAwait(false);
        return true;
    }

    private async Task SaveCoreAsync(TSaga saga, DateTimeOffset? timeoutAt, bool applyTimeout, string? executedStepName,
        IReadOnlyList<Entities.OutboxMessage>? messages, CancellationToken ct)
    {
        SagaInstance.EnsureValidCorrelationId(saga.CorrelationId);
        var now = _time.GetUtcNow();
        var own = session.Transaction is null ? session.BeginTransaction() : null;
        try
        {
            var existing = await session.Query<SagaInstanceRow>()
                .FirstOrDefaultAsync((r, a) => r.Id == a.Id && r.SagaType == a.Type, (Id: saga.Id, Type: SagaType), ct).ConfigureAwait(false);
            int version;
            if (existing is null)
            {
                var row = _records.New(new SagaInstanceRow(), saga, Tenant, now);
                if (applyTimeout)
                    row.TimeoutAt = timeoutAt;
                await session.InsertAsync(row, ct).ConfigureAwait(false);
                version = 0;
                LogSagaCreated(saga.Id, saga.CorrelationId);
            }
            else
            {
                var basedOn = _readVersions.TryGetValue(existing.Id, out var read) ? read : existing.Version;
                _records.Apply(existing, saga, now);
                version = basedOn + 1;
                var affected = applyTimeout
                    ? await session.Query<SagaInstanceRow>().Where((r, a) => r.Id == a.Id && r.Version == a.Version, (Id: saga.Id, Version: basedOn))
                        .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.State, a.State).SetProperty(r => r.StateData, a.Data).SetProperty(r => r.Status, a.Status)
                                .SetProperty(r => r.CompletedAt, a.CompletedAt).SetProperty(r => r.LastStepAt, a.LastStepAt).SetProperty(r => r.TimeoutAt, a.TimeoutAt).SetProperty(r => r.Version, a.Version),
                            (existing.State, Data: existing.StateData, existing.Status, existing.CompletedAt, existing.LastStepAt, TimeoutAt: timeoutAt, Version: version), ct)
                        .ConfigureAwait(false)
                    : await session.Query<SagaInstanceRow>().Where((r, a) => r.Id == a.Id && r.Version == a.Version, (Id: saga.Id, Version: basedOn))
                        .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.State, a.State).SetProperty(r => r.StateData, a.Data).SetProperty(r => r.Status, a.Status)
                                .SetProperty(r => r.CompletedAt, a.CompletedAt).SetProperty(r => r.LastStepAt, a.LastStepAt).SetProperty(r => r.Version, a.Version),
                            (existing.State, Data: existing.StateData, existing.Status, existing.CompletedAt, existing.LastStepAt, Version: version), ct)
                        .ConfigureAwait(false);
                if (affected == 0)
                {
                    // Another message transitioned this saga first: nothing this save wrote stays (the transaction is rolled back below).
                    LogSagaConcurrencyConflict(saga.Id, saga.CorrelationId);
                    throw new SagaConcurrencyException(saga.Id, saga.CorrelationId);
                }

                LogSagaUpdated(saga.Id, saga.State.ToString()!);
            }

            if (executedStepName is not null)
                await session.InsertAsync(SagaRecords<TSaga, TState>.Step(new SagaStepRow(), saga.Id, executedStepName, now), ct).ConfigureAwait(false);
            if (messages is not null)
                await session.InsertManyAsync([.. messages.Select(m => OutboxMessageRecords.From(new OutboxMessageRow(), m))], ct).ConfigureAwait(false);

            own?.Commit();
            // This repository's own save is the state it now holds: a second save from it is not a conflict.
            _readVersions[saga.Id] = version;
        }
        finally
        {
            own?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetExecutedStepNamesAsync(Guid sagaId, CancellationToken ct = default)
        => await session.Query<SagaStepRow>().Where((s, id) => s.SagaInstanceId == id, sagaId).Select(s => s.StepName).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TSaga>> GetActiveAsync(CancellationToken ct = default)
    {
        var rows = await session.Query<SagaInstanceRow>().Where((r, a) => r.SagaType == a.Type && r.Status == a.Active, (Type: SagaType, Active: (int)SagaStatus.Active))
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(_records.Read)];
    }

    /// <inheritdoc />
    public async Task SetTimeoutAsync(Guid sagaId, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => await session.Query<SagaInstanceRow>().Where((r, a) => r.Id == a.Id && r.SagaType == a.Type, (Id: sagaId, Type: SagaType))
            .ExecuteUpdateAsync((s, at) => s.SetProperty(r => r.TimeoutAt, at), timeoutAt, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TSaga>> GetDueForTimeoutAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var rows = await session.Query<SagaInstanceRow>()
            .Where((r, a) => r.SagaType == a.Type && r.Status == a.Active && r.TimeoutAt != null && r.TimeoutAt <= a.AsOf,
                (Type: SagaType, Active: (int)SagaStatus.Active, AsOf: (DateTimeOffset?)asOf))
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(_records.Read)];
    }

    /// <inheritdoc />
    public async Task MarkTimedOutAsync(Guid sagaId, CancellationToken ct = default)
    {
        // Active only: a saga a concurrent event already moved to a terminal status keeps it.
        var now = (DateTimeOffset?)_time.GetUtcNow();
        await session.Query<SagaInstanceRow>()
            .Where((r, a) => r.Id == a.Id && r.SagaType == a.Type && r.Status == a.Active, (Id: sagaId, Type: SagaType, Active: (int)SagaStatus.Active))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.Status, a.Status).SetProperty(r => r.CompletedAt, a.Now).SetProperty(r => r.LastStepAt, a.Now).SetProperty(r => r.TimeoutAt, a.Cleared),
                (Status: (int)SagaStatus.TimedOut, Now: now, Cleared: (DateTimeOffset?)null), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompensatedAsync(Guid sagaId, CancellationToken ct = default)
    {
        var now = (DateTimeOffset?)_time.GetUtcNow();
        await session.Query<SagaInstanceRow>().Where((r, a) => r.Id == a.Id && r.SagaType == a.Type, (Id: sagaId, Type: SagaType))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.Status, a.Status).SetProperty(r => r.CompletedAt, a.Now).SetProperty(r => r.LastStepAt, a.Now).SetProperty(r => r.TimeoutAt, a.Cleared),
                (Status: (int)SagaStatus.Compensated, Now: now, Cleared: (DateTimeOffset?)null), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFaultedAsync(Guid sagaId, string error, CancellationToken ct = default)
    {
        var now = (DateTimeOffset?)_time.GetUtcNow();
        await session.Query<SagaInstanceRow>().Where((r, a) => r.Id == a.Id && r.SagaType == a.Type, (Id: sagaId, Type: SagaType))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.Status, a.Status).SetProperty(r => r.LastError, a.Error).SetProperty(r => r.LastStepAt, a.Now),
                (Status: (int)SagaStatus.Faulted, Error: (string?)error, Now: now), ct).ConfigureAwait(false);
    }

    private TSaga Read(SagaInstanceRow row)
    {
        _readVersions[row.Id] = row.Version;
        return _records.Read(row);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saga {SagaId} created (correlation: {CorrelationId})")]
    private partial void LogSagaCreated(Guid sagaId, string correlationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saga {SagaId} updated → state {State}")]
    private partial void LogSagaUpdated(Guid sagaId, string state);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Saga {SagaId} (correlation: {CorrelationId}) concurrency conflict — saved by another writer first; the step is tried again on a fresh read")]
    private partial void LogSagaConcurrencyConflict(Guid sagaId, string correlationId);
}
