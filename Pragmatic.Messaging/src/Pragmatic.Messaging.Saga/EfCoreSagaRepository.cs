using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Saga;

/// <summary>
///     EF Core-backed saga repository. Persists saga state as JSON in <see cref="SagaInstance"/>.
///     Mark the saga's <c>[Boundary]</c> with <c>[EnableSagaPersistence]</c> to register this instead of
///     <see cref="InMemorySagaRepository{TSaga}"/> (the generator wires it against the boundary's DbContext).
/// </summary>
/// <typeparam name="TSaga">The concrete saga type implementing <see cref="ISaga{TState}"/>.</typeparam>
/// <typeparam name="TState">The enum type representing saga states.</typeparam>
public sealed partial class EfCoreSagaRepository<TSaga, TState>(
    DbContext dbContext,
    ILogger<EfCoreSagaRepository<TSaga, TState>> logger,
    PragmaticJsonOptions? jsonOptions = null,
    ITenantContext? tenantContext = null) : ISagaRepository<TSaga>
    where TSaga : class, ISaga<TState>, new()
    where TState : struct, Enum
{
    private static readonly string SagaTypeName = typeof(TSaga).FullName ?? typeof(TSaga).Name;

    // Built once. Serializing through a JsonTypeInfo<T> (not the options overload) is AOT-clean at the API
    // level — no IL2026/IL3050 — and source-gen-backed when the app registered its PragmaticJsonContext (the
    // [Saga] state + message types are contributed to it). Mirrors OutboxInterceptor's GetTypeInfo path.
    private readonly JsonSerializerOptions _jsonOptions = (jsonOptions ?? PragmaticJsonOptions.Default).Build();

    // STJ caches JsonTypeInfo per options instance, so this resolve is cheap on repeat.
    private JsonTypeInfo<TSaga> SagaTypeInfo => (JsonTypeInfo<TSaga>)_jsonOptions.GetTypeInfo(typeof(TSaga));

    // The Version each saga's state was read with. Reads are AsNoTracking and a save queries the row again,
    // so without this the concurrency check would compare against the version read at save time — and a
    // writer whose state predates another's save would win and erase it.
    private readonly Dictionary<Guid, int> _readVersions = [];

    /// <inheritdoc />
    public async Task<TSaga?> FindByCorrelationAsync(string correlationId, CancellationToken ct = default)
    {
        // Returns the instance for the correlation regardless of Status so callers can also INSPECT a
        // terminal saga (Completed/Compensated/…). Re-processing a terminal saga on a redelivery is
        // prevented by the handler transitioning to a terminal state on rejection (the recommended
        // pattern — the event then matches no step) plus the rule that a transient fault does not compensate.
        // A handler that rejects WITHOUT moving to a terminal state could be re-processed on redelivery —
        // documented in saga-guide.md.
        var instance = await dbContext.Set<SagaInstance>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SagaType == SagaTypeName && s.CorrelationId == correlationId, ct)
            .ConfigureAwait(false);

        return instance is null ? null : Read(instance);
    }

    /// <inheritdoc />
    public async Task<TSaga?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await dbContext.Set<SagaInstance>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.SagaType == SagaTypeName, ct)
            .ConfigureAwait(false);

        return instance is null ? null : Read(instance);
    }

    /// <inheritdoc />
    public Task SaveAsync(TSaga saga, CancellationToken ct = default)
        => SaveCoreAsync(saga, timeoutAt: null, applyTimeout: false, executedStepName: null, ct);

    /// <inheritdoc />
    public Task SaveWithTimeoutAsync(TSaga saga, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => SaveCoreAsync(saga, timeoutAt, applyTimeout: true, executedStepName: null, ct);

    /// <inheritdoc />
    public Task SaveWithStepAsync(TSaga saga, DateTimeOffset? timeoutAt, string executedStepName, CancellationToken ct = default)
        => SaveCoreAsync(saga, timeoutAt, applyTimeout: true, executedStepName, ct);

    /// <inheritdoc />
    public async Task<bool> SaveWithStepAndOutboxAsync(
        TSaga saga, DateTimeOffset? timeoutAt, string executedStepName,
        IReadOnlyList<object> pendingMessages, CancellationToken ct = default)
    {
        // Route the step's resulting action through the transactional outbox only when this boundary's
        // DbContext actually maps __OutboxMessages (i.e. the boundary is [EnableOutbox]). Otherwise fall
        // back to save-before-publish (return false → the orchestrator publishes inline).
        var hasOutbox = dbContext.Model.FindEntityType(typeof(OutboxMessage)) is not null;

        if (pendingMessages.Count == 0)
        {
            // Nothing to deliver — a plain atomic save. "Delivered" either way (the caller has nothing to publish).
            await SaveWithStepAsync(saga, timeoutAt, executedStepName, ct).ConfigureAwait(false);
            return true;
        }

        if (!hasOutbox)
        {
            await SaveWithStepAsync(saga, timeoutAt, executedStepName, ct).ConfigureAwait(false);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var rows = new List<OutboxMessage>(pendingMessages.Count);
        foreach (var message in pendingMessages)
        {
            var type = message.GetType();
            rows.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = type.FullName ?? type.Name,
                // AOT-clean when the message type is in the app's PragmaticJsonContext (mirrors OutboxInterceptor).
                Payload = JsonSerializer.Serialize(message, _jsonOptions.GetTypeInfo(type)),
                CreatedAt = now,
                CorrelationId = saga.CorrelationId,
                TenantId = tenantContext?.TenantId,
            });
        }

        await SaveCoreAsync(saga, timeoutAt, applyTimeout: true, executedStepName, ct, rows).ConfigureAwait(false);
        return true;
    }

    private async Task SaveCoreAsync(
        TSaga saga, DateTimeOffset? timeoutAt, bool applyTimeout, string? executedStepName, CancellationToken ct,
        IReadOnlyList<OutboxMessage>? outboxRows = null)
    {
        SagaInstance.EnsureValidCorrelationId(saga.CorrelationId);

        var existing = await dbContext.Set<SagaInstance>()
            .FirstOrDefaultAsync(s => s.Id == saga.Id && s.SagaType == SagaTypeName, ct)
            .ConfigureAwait(false);

        // Everything this save puts in the change tracker, so a save that loses can take it out again.
        var prepared = new List<object>();

        if (existing is null)
        {
            var instance = ToInstance(saga);
            // Persist the next step deadline in the SAME insert as the state (atomic).
            if (applyTimeout)
                instance.TimeoutAt = timeoutAt;
            // Record the executed step in the SAME commit as the state — the history and the
            // state can never diverge, and a concurrency-losing save discards the step row too.
            if (executedStepName is not null)
            {
                var step = NewStep(instance.Id, executedStepName);
                instance.Steps.Add(step);
                prepared.Add(step);
            }
            dbContext.Set<SagaInstance>().Add(instance);
            prepared.Add(instance);
            LogSagaCreated(saga.Id, saga.CorrelationId);
        }
        else
        {
            existing.State = ToInt(saga.State);
            existing.StateData = JsonSerializer.Serialize(saga, SagaTypeInfo);
            existing.Status = saga.CompletedAt.HasValue ? SagaStatus.Completed : SagaStatus.Active;
            existing.CompletedAt = saga.CompletedAt;
            existing.LastStepAt = DateTimeOffset.UtcNow;
            // Persist the next step deadline in the SAME commit as the state (atomic) — a crash
            // cannot leave the saga with an advanced state but a stale/missing deadline.
            if (applyTimeout)
                existing.TimeoutAt = timeoutAt;
            if (executedStepName is not null)
            {
                var step = NewStep(existing.Id, executedStepName);
                dbContext.Set<SagaStep>().Add(step);
                prepared.Add(step);
            }
            prepared.Add(existing);
            // Bump the concurrency token: EF puts the ORIGINAL value in the UPDATE's WHERE clause, so a
            // racing writer that read the same version loses (0 rows affected → DbUpdateConcurrencyException).
            // The original is the version the state was READ with, when this repository read it — not the
            // one just re-queried, which already includes any save made since.
            var basedOn = existing.Version;
            if (_readVersions.TryGetValue(existing.Id, out var readVersion))
            {
                dbContext.Entry(existing).Property(e => e.Version).OriginalValue = readVersion;
                basedOn = readVersion;
            }
            existing.Version = basedOn + 1;
            LogSagaUpdated(saga.Id, saga.State.ToString()!);
        }

        // Transactional outbox: the step's resulting messages are written in the SAME SaveChanges as the
        // saga state, so the action and the state commit atomically (exactly-once delivery via the pump).
        if (outboxRows is not null)
        {
            dbContext.Set<OutboxMessage>().AddRange(outboxRows);
            prepared.AddRange(outboxRows);
        }

        try
        {
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            // This repository's own save is the state it now holds: a second save from it is not a conflict.
            _readVersions[saga.Id] = existing?.Version ?? 0;
        }
        catch (DbUpdateConcurrencyException conflict)
        {
            // Another message transitioned this saga first. The orchestrator reads it again and re-runs the
            // step in this same context, so what this save prepared must not stay behind: left
            // tracked, the step row and the outbox messages would be inserted again with the retry's own.
            foreach (var entity in prepared)
                dbContext.Entry(entity).State = EntityState.Detached;

            LogSagaConcurrencyConflict(saga.Id, saga.CorrelationId);
            throw new SagaConcurrencyException(saga.Id, saga.CorrelationId, conflict);
        }
    }

    private static SagaStep NewStep(Guid sagaInstanceId, string stepName)
    {
        var now = DateTimeOffset.UtcNow;
        return new SagaStep
        {
            Id = Guid.NewGuid(),
            SagaInstanceId = sagaInstanceId,
            StepName = stepName,
            Status = SagaStepStatus.Completed,
            StartedAt = now,
            CompletedAt = now,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetExecutedStepNamesAsync(Guid sagaId, CancellationToken ct = default)
    {
        // Membership set for path-based compensation: order is irrelevant (the orchestrator
        // dispatches compensators in reverse declaration order, filtered by this set).
        return await dbContext.Set<SagaStep>()
            .AsNoTracking()
            .Where(s => s.SagaInstanceId == sagaId)
            .Select(s => s.StepName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TSaga>> GetActiveAsync(CancellationToken ct = default)
    {
        // Cross-tenant by design: background timeout orchestration and the ops dashboard need every
        // tenant's active sagas, and this runs with no ambient tenant (fail-closed filter → 0 rows).
        var instances = await dbContext.Set<SagaInstance>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.SagaType == SagaTypeName && s.Status == SagaStatus.Active)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return instances.Select(Read).ToList();
    }

    /// <inheritdoc />
    public async Task SetTimeoutAsync(Guid sagaId, DateTimeOffset? timeoutAt, CancellationToken ct = default)
    {
        // Targeted UPDATE keyed on the globally-unique Id — bypass the tenant filter so the timeout
        // orchestrator (no ambient tenant) can move the deadline.
        await dbContext.Set<SagaInstance>()
            .IgnoreQueryFilters()
            .Where(s => s.Id == sagaId && s.SagaType == SagaTypeName)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TimeoutAt, timeoutAt), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TSaga>> GetDueForTimeoutAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        // Cross-tenant background scan (see GetActiveAsync).
        var instances = await dbContext.Set<SagaInstance>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.SagaType == SagaTypeName
                && s.Status == SagaStatus.Active
                && s.TimeoutAt != null
                && s.TimeoutAt <= asOf)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return instances.Select(Read).ToList();
    }

    /// <inheritdoc />
    public async Task MarkTimedOutAsync(Guid sagaId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Guard on Status == Active: a saga that a concurrent event already advanced to a terminal
        // status (Completed/Compensated) must not be overwritten with TimedOut. GetDueForTimeout
        // already filters Active, but this closes the race between the scan and this update.
        await dbContext.Set<SagaInstance>()
            .IgnoreQueryFilters()
            .Where(s => s.Id == sagaId && s.SagaType == SagaTypeName && s.Status == SagaStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SagaStatus.TimedOut)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LastStepAt, now)
                .SetProperty(x => x.TimeoutAt, (DateTimeOffset?)null), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompensatedAsync(Guid sagaId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await dbContext.Set<SagaInstance>()
            .IgnoreQueryFilters()
            .Where(s => s.Id == sagaId && s.SagaType == SagaTypeName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SagaStatus.Compensated)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LastStepAt, now)
                .SetProperty(x => x.TimeoutAt, (DateTimeOffset?)null), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFaultedAsync(Guid sagaId, string error, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await dbContext.Set<SagaInstance>()
            .IgnoreQueryFilters()
            .Where(s => s.Id == sagaId && s.SagaType == SagaTypeName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SagaStatus.Faulted)
                .SetProperty(x => x.LastError, error)
                .SetProperty(x => x.LastStepAt, now), ct)
            .ConfigureAwait(false);
    }

    // ── Mapping ──

    private SagaInstance ToInstance(TSaga saga) => new()
    {
        Id = saga.Id,
        SagaType = SagaTypeName,
        CorrelationId = saga.CorrelationId,
        State = ToInt(saga.State),
        StateData = JsonSerializer.Serialize(saga, SagaTypeInfo),
        Status = saga.CompletedAt.HasValue ? SagaStatus.Completed : SagaStatus.Active,
        StartedAt = saga.StartedAt,
        CompletedAt = saga.CompletedAt,
        LastStepAt = DateTimeOffset.UtcNow,
        // Stamp the ambient tenant so the row is owned by the current tenant and matches the
        // fail-closed tenant query filter on read. Empty in single-tenant hosts (no filter emitted).
        TenantId = tenantContext?.TenantId ?? string.Empty
    };

    private TSaga Read(SagaInstance instance)
    {
        _readVersions[instance.Id] = instance.Version;
        return Deserialize(instance);
    }

    private TSaga Deserialize(SagaInstance instance)
    {
        TSaga saga;
        if (!string.IsNullOrEmpty(instance.StateData))
        {
            saga = JsonSerializer.Deserialize(instance.StateData, SagaTypeInfo)!;
        }
        else
        {
            saga = new TSaga();
        }

        // Ensure core properties are synced from the instance row
        saga.Id = instance.Id;
        saga.CorrelationId = instance.CorrelationId;
        saga.State = FromInt(instance.State);
        saga.StartedAt = instance.StartedAt;
        saga.CompletedAt = instance.CompletedAt;

        return saga;
    }

    private static int ToInt(TState state) => (int)(object)state;
    private static TState FromInt(int value) => (TState)(object)value;

    // ── Logging ──

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Saga {SagaId} created (correlation: {CorrelationId})")]
    partial void LogSagaCreated(Guid sagaId, string correlationId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Saga {SagaId} updated → state {State}")]
    partial void LogSagaUpdated(Guid sagaId, string state);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Saga {SagaId} (correlation: {CorrelationId}) concurrency conflict — saved by another writer first; the step is tried again on a fresh read")]
    partial void LogSagaConcurrencyConflict(Guid sagaId, string correlationId);
}
