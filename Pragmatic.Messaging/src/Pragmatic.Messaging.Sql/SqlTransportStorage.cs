using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Sql.Entities;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     The transport's storage engine: enqueue with publish fan-out, batch claim (portable EF
///     CAS, three phases like the outbox), ack (DELETE with claim-token guard), nack (backoff on
///     <c>VisibleAt</c> or transactional MOVE to the dead-letter table), durable subscription
///     registry with a small publish-side cache, and restart-safe schedule cancellation.
/// </summary>
public sealed partial class SqlTransportStorage(
    IDbContextFactory<SqlTransportDbContext> contextFactory,
    SqlTransportOptions options,
    ILogger<SqlTransportStorage> logger)
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    private readonly object _cacheLock = new();
    private Dictionary<string, List<string>>? _subscriptionCache;
    private DateTimeOffset _subscriptionCacheExpires;

    // ── Enqueue ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Publish fan-out: one row per subscription registered on the topic, one transaction.
    ///     Returns the subscription queue names enqueued (empty = no subscribers, message
    ///     dropped — broker semantics; registrations are durable so this only happens before
    ///     the first bind). The caller wakes exactly these queues (pg_notify).
    /// </summary>
    public async Task<IReadOnlyList<string>> PublishAsync(
        byte[] payload, string topic, MessageContext context,
        DateTimeOffset? visibleAt = null, Guid? schedulingToken = null, CancellationToken ct = default)
    {
        var subscriptions = await GetSubscriptionsAsync(topic, ct).ConfigureAwait(false);
        if (subscriptions.Count == 0)
        {
            LogNoSubscribers(topic);
            return [];
        }

        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            foreach (var subscription in subscriptions)
                db.Messages.Add(CreateRow(payload, subscription, topic, context, visibleAt, schedulingToken));

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return subscriptions;
    }

    /// <summary>Point-to-point enqueue on a single queue.</summary>
    public async Task SendAsync(
        byte[] payload, string queue, MessageContext context,
        DateTimeOffset? visibleAt = null, Guid? schedulingToken = null, CancellationToken ct = default)
    {
        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            db.Messages.Add(CreateRow(payload, queue, topic: null, context, visibleAt, schedulingToken));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private static TransportMessage CreateRow(
        byte[] payload, string queueName, string? topic, MessageContext context,
        DateTimeOffset? visibleAt, Guid? schedulingToken)
    {
        var now = DateTimeOffset.UtcNow;
        return new TransportMessage
        {
            MessageId = context.MessageId,
            QueueName = queueName,
            Topic = topic,
            Payload = payload,
            CorrelationId = context.CorrelationId,
            TenantId = context.TenantId,
            UserId = context.UserId,
            SourceBoundary = context.SourceBoundary,
            BusName = context.BusName,
            HeadersJson = context.Headers is null
                ? null
                : JsonSerializer.Serialize(
                    context.Headers.ToDictionary(kv => kv.Key, kv => kv.Value),
                    SqlTransportJsonContext.Default.DictionaryStringString),
            EnqueuedAt = now,
            VisibleAt = visibleAt ?? now,
            SchedulingTokenId = schedulingToken,
        };
    }

    // ── Claim / Ack / Nack ───────────────────────────────────────────────────

    /// <summary>
    ///     Claims up to <c>BatchSize</c> eligible rows for a queue: three-phase portable CAS
    ///     (select ids → conditional update stamping the claim token and incrementing
    ///     DeliveryCount → re-select winners). Eligible = visible and unlocked/lease-expired.
    /// </summary>
    public async Task<IReadOnlyList<TransportMessage>> ClaimBatchAsync(string queue, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var token = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Phase 1: candidates (Take inside ExecuteUpdate is not translatable on every provider)
            var candidateIds = await db.Messages
                .Where(m => m.QueueName == queue
                    && m.VisibleAt <= now
                    && (m.LockedBy == null || m.LockExpiresAt == null || m.LockExpiresAt < now))
                .OrderBy(m => m.Id)
                .Take(options.BatchSize)
                .Select(m => m.Id)
                .ToListAsync(ct).ConfigureAwait(false);

            if (candidateIds.Count == 0)
                return [];

            // Phase 2: CAS — re-check eligibility inside the UPDATE so racing replicas lose cleanly.
            var lockUntil = now + options.LockDuration;
            await db.Messages
                .Where(m => candidateIds.Contains(m.Id)
                    && m.VisibleAt <= now
                    && (m.LockedBy == null || m.LockExpiresAt == null || m.LockExpiresAt < now))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(m => m.LockedBy, token)
                    .SetProperty(m => m.LockExpiresAt, lockUntil)
                    .SetProperty(m => m.DeliveryCount, m => m.DeliveryCount + 1), ct)
                .ConfigureAwait(false);

            // Phase 3: winners
            return await db.Messages
                .AsNoTracking()
                .Where(m => m.LockedBy == token)
                .OrderBy(m => m.Id)
                .ToListAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Ack = DELETE guarded by the claim token. Zero rows = the lease expired and another
    ///     replica reclaimed the message (duplicate covered by the idempotency store) — warn.
    /// </summary>
    public async Task AckAsync(long id, string claimToken, CancellationToken ct = default)
    {
        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var deleted = await db.Messages
                .Where(m => m.Id == id && m.LockedBy == claimToken)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);

            if (deleted == 0)
                LogAckLostLease(id);
        }
    }

    /// <summary>
    ///     Nack: below <c>MaxDeliveryCount</c> → release the lock and push <c>VisibleAt</c>
    ///     forward with exponential backoff; at/over the limit → transactional MOVE to the
    ///     dead-letter table.
    /// </summary>
    public async Task NackAsync(TransportMessage message, string error, CancellationToken ct = default)
    {
        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (message.DeliveryCount >= options.MaxDeliveryCount)
            {
                var moved = 0;
                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                    await using (tx.ConfigureAwait(false))
                    {
                        db.DeadLetters.Add(new TransportDeadLetter
                        {
                            Id = message.Id,
                            MessageId = message.MessageId,
                            QueueName = message.QueueName,
                            Topic = message.Topic,
                            Payload = message.Payload,
                            CorrelationId = message.CorrelationId,
                            TenantId = message.TenantId,
                            UserId = message.UserId,
                            SourceBoundary = message.SourceBoundary,
                            BusName = message.BusName,
                            HeadersJson = message.HeadersJson,
                            EnqueuedAt = message.EnqueuedAt,
                            DeliveryCount = message.DeliveryCount,
                            LastError = Truncate(error),
                            DeadLetteredAt = DateTimeOffset.UtcNow,
                            Reason = "MaxDeliveryCountExceeded",
                        });
                        await db.SaveChangesAsync(ct).ConfigureAwait(false);

                        moved = await db.Messages
                            .Where(m => m.Id == message.Id)
                            .ExecuteDeleteAsync(ct).ConfigureAwait(false);

                        await tx.CommitAsync(ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

                if (moved > 0)
                    LogDeadLettered(message.QueueName, message.MessageId, message.DeliveryCount);
                return;
            }

            var backoffExponent = Math.Min(message.DeliveryCount, 16);
            var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, backoffExponent), MaxBackoff.TotalSeconds));
            var visibleAt = DateTimeOffset.UtcNow + backoff;
            var truncated = Truncate(error);

            await db.Messages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(m => m.LockedBy, (string?)null)
                    .SetProperty(m => m.LockExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(m => m.VisibleAt, visibleAt)
                    .SetProperty(m => m.LastError, truncated), ct)
                .ConfigureAwait(false);
        }
    }

    private static string Truncate(string error)
        => error.Length <= 2048 ? error : error[..2048];

    // ── Subscription registry ────────────────────────────────────────────────

    /// <summary>Durable upsert (unbinding a consumer never deletes the registration).</summary>
    public async Task UpsertSubscriptionAsync(string topic, string subscriptionName, CancellationToken ct = default)
    {
        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var updated = await db.Subscriptions
                .Where(s => s.Topic == topic && s.SubscriptionName == subscriptionName)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastActiveAt, now), ct)
                .ConfigureAwait(false);

            if (updated == 0)
            {
                db.Subscriptions.Add(new TransportSubscription
                {
                    Topic = topic,
                    SubscriptionName = subscriptionName,
                    CreatedAt = now,
                    LastActiveAt = now,
                });
                try
                {
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    // Unique-index race with another replica: the registration exists — done.
                }
            }
        }

        InvalidateSubscriptionCache();
    }

    private async Task<List<string>> GetSubscriptionsAsync(string topic, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_cacheLock)
        {
            if (_subscriptionCache is not null && _subscriptionCacheExpires > now)
                return _subscriptionCache.TryGetValue(topic, out var cached) ? cached : [];
        }

        Dictionary<string, List<string>> fresh;
        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var all = await db.Subscriptions
                .AsNoTracking()
                .Select(s => new { s.Topic, s.SubscriptionName })
                .ToListAsync(ct).ConfigureAwait(false);

            fresh = all
                .GroupBy(s => s.Topic, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(s => s.SubscriptionName).ToList(), StringComparer.Ordinal);
        }

        lock (_cacheLock)
        {
            _subscriptionCache = fresh;
            _subscriptionCacheExpires = now + options.SubscriptionCacheTtl;
        }

        return fresh.TryGetValue(topic, out var subscriptions) ? subscriptions : [];
    }

    internal void InvalidateSubscriptionCache()
    {
        lock (_cacheLock)
        {
            _subscriptionCache = null;
        }
    }

    // ── Scheduling ───────────────────────────────────────────────────────────

    /// <summary>Restart-safe cancel: deletes all UNCLAIMED rows of a scheduled publish.</summary>
    public async Task<int> CancelScheduledAsync(Guid schedulingToken, CancellationToken ct = default)
    {
        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Messages
                .Where(m => m.SchedulingTokenId == schedulingToken && m.LockedBy == null)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
    }

    // ── LoggerMessage ────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Publish to topic {Topic} dropped: no subscriptions registered yet (registrations are durable — this only happens before the first consumer bind)")]
    private partial void LogNoSubscribers(string topic);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Ack for message row {Id} matched nothing — the lease expired and the row was reclaimed elsewhere (at-least-once duplicate, covered by idempotency)")]
    private partial void LogAckLostLease(long id);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Message {MessageId} on queue {Queue} dead-lettered after {DeliveryCount} deliveries")]
    private partial void LogDeadLettered(string queue, string messageId, int deliveryCount);
}
