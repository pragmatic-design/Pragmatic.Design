using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Saga;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Engines.Documents;

/// <summary>
///     The saga repository of an <c>[EnableSagaPersistence]</c> boundary the documents engine serves, over the boundary's session: what
///     <c>EfCoreSagaRepository</c> does, on collections of the same names. The instance is updated only at the version its state was read with,
///     so a writer that read it before another's save loses (<see cref="SagaConcurrencyException" />).
/// </summary>
/// <remarks>
///     <para>
///         One save writes the instance, the executed step and, when the boundary is <c>[EnableOutbox]</c> (<paramref name="outbox" />), the
///         step's messages. Where the server runs multi-document transactions (MongoDB on a replica set or a sharded cluster) they are one
///         transaction, as on the other engines. Where it does not (Cosmos DB, a standalone MongoDB) they are three writes in that order: the
///         instance's conditional update is the commit point, so a saga that loses the race writes nothing, and a failure after it leaves the
///         state advanced with its step record or its messages missing.
///     </para>
///     <para>
///         The reads by id and by correlation filter on the ambient tenant (empty without one), as EF Core's tenant filter does; the background
///         reads and the marks are cross-tenant, as there.
///     </para>
/// </remarks>
public sealed partial class DocSagaRepository<TSaga, TState>(
    DocSession session,
    ILogger<DocSagaRepository<TSaga, TState>> logger,
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
    private readonly Dictionary<Guid, int> _readVersions = [];

    private string Tenant => tenantContext?.TenantId ?? "";

    /// <inheritdoc />
    public async Task<TSaga?> FindByCorrelationAsync(string correlationId, CancellationToken ct = default)
    {
        var document = await session.Query<SagaInstanceDocument>()
            .FirstOrDefaultAsync((d, a) => d.SagaType == a.Type && d.CorrelationId == a.Correlation && d.TenantId == a.Tenant,
                (Type: SagaType, Correlation: correlationId, Tenant: Tenant), ct).ConfigureAwait(false);
        return document is null ? null : Read(document);
    }

    /// <inheritdoc />
    public async Task<TSaga?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var document = await session.Query<SagaInstanceDocument>()
            .FirstOrDefaultAsync((d, a) => d.Id == a.Id && d.SagaType == a.Type && d.TenantId == a.Tenant, (Id: id, Type: SagaType, Tenant: Tenant), ct)
            .ConfigureAwait(false);
        return document is null ? null : Read(document);
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
        var own = session.Transaction is null && await session.SupportsTransactionsAsync(ct).ConfigureAwait(false) ? session.BeginTransaction() : null;
        try
        {
            var existing = await session.Query<SagaInstanceDocument>()
                .FirstOrDefaultAsync((d, a) => d.Id == a.Id && d.SagaType == a.Type, (Id: saga.Id, Type: SagaType), ct).ConfigureAwait(false);
            int version;
            if (existing is null)
            {
                var document = _records.New(new SagaInstanceDocument(), saga, Tenant, now);
                if (applyTimeout)
                    document.TimeoutAt = timeoutAt?.UtcTicks;
                await session.InsertAsync(document, ct).ConfigureAwait(false);
                version = 0;
                LogSagaCreated(saga.Id, saga.CorrelationId);
            }
            else
            {
                var basedOn = _readVersions.TryGetValue(existing.Id, out var read) ? read : existing.Version;
                _records.Apply(existing, saga, now);
                version = basedOn + 1;
                var affected = applyTimeout
                    ? await session.Query<SagaInstanceDocument>().Where((d, a) => d.Id == a.Id && d.Version == a.Version, (Id: saga.Id, Version: basedOn))
                        .ExecuteUpdateAsync((s, a) => s.Set(d => d.State, a.State).Set(d => d.StateData, a.Data).Set(d => d.Status, a.Status).Set(d => d.Ended, a.Ended)
                                .Set(d => d.CompletedAt, a.CompletedAt).Set(d => d.LastStepAt, a.LastStepAt).Set(d => d.TimeoutAt, a.TimeoutAt).Set(d => d.Version, a.Version),
                            (existing.State, Data: existing.StateData, existing.Status, existing.Ended, existing.CompletedAt, existing.LastStepAt,
                                TimeoutAt: timeoutAt?.UtcTicks, Version: version), ct)
                        .ConfigureAwait(false)
                    : await session.Query<SagaInstanceDocument>().Where((d, a) => d.Id == a.Id && d.Version == a.Version, (Id: saga.Id, Version: basedOn))
                        .ExecuteUpdateAsync((s, a) => s.Set(d => d.State, a.State).Set(d => d.StateData, a.Data).Set(d => d.Status, a.Status).Set(d => d.Ended, a.Ended)
                                .Set(d => d.CompletedAt, a.CompletedAt).Set(d => d.LastStepAt, a.LastStepAt).Set(d => d.Version, a.Version),
                            (existing.State, Data: existing.StateData, existing.Status, existing.Ended, existing.CompletedAt, existing.LastStepAt, Version: version), ct)
                        .ConfigureAwait(false);
                if (affected == 0)
                {
                    LogSagaConcurrencyConflict(saga.Id, saga.CorrelationId);
                    throw new SagaConcurrencyException(saga.Id, saga.CorrelationId);
                }

                LogSagaUpdated(saga.Id, saga.State.ToString()!);
            }

            if (executedStepName is not null)
                await session.InsertAsync(SagaRecords<TSaga, TState>.Step(new SagaStepDocument(), saga.Id, executedStepName, now), ct).ConfigureAwait(false);
            if (messages is not null)
                await session.InsertManyAsync([.. messages.Select(m => OutboxMessageRecords.From(new OutboxMessageDocument(), m))], ct).ConfigureAwait(false);

            if (own is not null)
                await own.CommitAsync(ct).ConfigureAwait(false);
            _readVersions[saga.Id] = version;
        }
        finally
        {
            if (own is not null)
                await own.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetExecutedStepNamesAsync(Guid sagaId, CancellationToken ct = default)
        => await session.Query<SagaStepDocument>().Where((s, id) => s.SagaInstanceId == id, sagaId).Select(s => s.StepName).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TSaga>> GetActiveAsync(CancellationToken ct = default)
    {
        var documents = await session.Query<SagaInstanceDocument>().Where((d, a) => d.SagaType == a.Type && d.Status == a.Active, (Type: SagaType, Active: (int)SagaStatus.Active))
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. documents.Select(_records.Read)];
    }

    /// <inheritdoc />
    public async Task SetTimeoutAsync(Guid sagaId, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => await session.Query<SagaInstanceDocument>().Where((d, a) => d.Id == a.Id && d.SagaType == a.Type, (Id: sagaId, Type: SagaType))
            .ExecuteUpdateAsync((s, at) => s.Set(d => d.TimeoutAt, at), timeoutAt?.UtcTicks, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TSaga>> GetDueForTimeoutAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var documents = await session.Query<SagaInstanceDocument>()
            .Where((d, a) => d.SagaType == a.Type && d.Status == a.Active && d.TimeoutAt != null && d.TimeoutAt <= a.AsOf,
                (Type: SagaType, Active: (int)SagaStatus.Active, AsOf: (long?)asOf.UtcTicks))
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. documents.Select(_records.Read)];
    }

    /// <inheritdoc />
    public async Task MarkTimedOutAsync(Guid sagaId, CancellationToken ct = default)
    {
        // Active only: a saga a concurrent event already moved to a terminal status keeps it.
        var now = (long?)_time.GetUtcNow().UtcTicks;
        await session.Query<SagaInstanceDocument>()
            .Where((d, a) => d.Id == a.Id && d.SagaType == a.Type && d.Status == a.Active, (Id: sagaId, Type: SagaType, Active: (int)SagaStatus.Active))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.Status, a.Status).Set(d => d.Ended, true).Set(d => d.CompletedAt, a.Now).Set(d => d.LastStepAt, a.Now)
                .Set(d => d.TimeoutAt, a.Cleared), (Status: (int)SagaStatus.TimedOut, Now: now, Cleared: (long?)null), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompensatedAsync(Guid sagaId, CancellationToken ct = default)
    {
        var now = (long?)_time.GetUtcNow().UtcTicks;
        await session.Query<SagaInstanceDocument>().Where((d, a) => d.Id == a.Id && d.SagaType == a.Type, (Id: sagaId, Type: SagaType))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.Status, a.Status).Set(d => d.Ended, true).Set(d => d.CompletedAt, a.Now).Set(d => d.LastStepAt, a.Now)
                .Set(d => d.TimeoutAt, a.Cleared), (Status: (int)SagaStatus.Compensated, Now: now, Cleared: (long?)null), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFaultedAsync(Guid sagaId, string error, CancellationToken ct = default)
    {
        var now = (long?)_time.GetUtcNow().UtcTicks;
        await session.Query<SagaInstanceDocument>().Where((d, a) => d.Id == a.Id && d.SagaType == a.Type, (Id: sagaId, Type: SagaType))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.Status, a.Status).Set(d => d.Ended, true).Set(d => d.LastError, a.Error).Set(d => d.LastStepAt, a.Now),
                (Status: (int)SagaStatus.Faulted, Error: (string?)error, Now: now), ct).ConfigureAwait(false);
    }

    private TSaga Read(SagaInstanceDocument document)
    {
        _readVersions[document.Id] = document.Version;
        return _records.Read(document);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saga {SagaId} created (correlation: {CorrelationId})")]
    private partial void LogSagaCreated(Guid sagaId, string correlationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saga {SagaId} updated → state {State}")]
    private partial void LogSagaUpdated(Guid sagaId, string state);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Saga {SagaId} (correlation: {CorrelationId}) concurrency conflict — saved by another writer first; the step is tried again on a fresh read")]
    private partial void LogSagaConcurrencyConflict(Guid sagaId, string correlationId);
}
