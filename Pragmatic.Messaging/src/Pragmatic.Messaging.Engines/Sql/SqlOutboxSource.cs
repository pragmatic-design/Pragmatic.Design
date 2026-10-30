using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Entities;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Messaging.Engines.Sql;

/// <summary>
///     The <see cref="IOutboxSource" /> of a boundary the Sql engine serves, read through the boundary's own session: what
///     <c>EfCoreOutboxSource</c> does on a <c>DbContext</c>, under <see cref="OutboxClaimPolicy" />'s rules. The pending rows' keys are read (oldest
///     first, past their backoff, a batch), claimed with one conditional update (pending, and unclaimed or past its lease: another worker's claim
///     stands), and the rows that carry this poll's token returned.
/// </summary>
public sealed partial class SqlOutboxSource(SqlSession session, string boundaryName, ILogger<SqlOutboxSource> logger, TimeProvider? time = null) : IOutboxSource
{
    private const int MaxErrorLength = 2048;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public string BoundaryName => boundaryName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcTicks;
        var token = OutboxClaimPolicy.NewClaimToken();

        // The keys first, then the claim by key: a batch's Take is no part of an UPDATE on every database.
        var candidates = await session.Query<OutboxMessageRow>()
            .Where((r, n) => r.ProcessedAt == null && (r.NextAttemptAt == null || r.NextAttemptAt <= n)
                && (r.ClaimedBy == null || r.ClaimedUntil == null || r.ClaimedUntil < n), now)
            .OrderBy(r => r.CreatedAt).Take(batchSize).Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return [];

        // The eligibility is checked again inside the update, so a candidate another worker claimed in between is left to it.
        var claimed = await session.Query<OutboxMessageRow>()
            .Where((r, a) => a.Ids.Contains(r.Id) && r.ProcessedAt == null && (r.ClaimedBy == null || r.ClaimedUntil == null || r.ClaimedUntil < a.Now),
                (Ids: candidates, Now: now))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.ClaimedBy, a.Token).SetProperty(r => r.ClaimedUntil, a.Until),
                (Token: token, Until: (long?)(now + OutboxClaimPolicy.ClaimLease.Ticks)), ct).ConfigureAwait(false);
        if (claimed == 0)
            return [];

        var rows = await session.Query<OutboxMessageRow>().Where((r, t) => r.ClaimedBy == t && r.ProcessedAt == null, token)
            .OrderBy(r => r.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(OutboxMessageRecords.ToMessage)];
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default)
    {
        var affected = await session.Query<OutboxMessageRow>().Where((r, id) => r.Id == id, messageId)
            .ExecuteUpdateAsync((s, at) => s.SetProperty(r => r.ProcessedAt, at), (long?)_time.GetUtcNow().UtcTicks, ct).ConfigureAwait(false);
        if (affected == 0)
            LogMessageNotFound(messageId, "MarkProcessed");
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        var row = await session.Query<OutboxMessageRow>().FirstOrDefaultAsync((r, id) => r.Id == id, messageId, ct).ConfigureAwait(false);
        if (row is null)
        {
            LogMessageNotFound(messageId, "MarkFailed");
            return;
        }

        row.RetryCount++;
        // Longer than the column would fail the write and leave the row claimed until its lease runs out.
        row.Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        row.NextAttemptAt = (_time.GetUtcNow() + OutboxClaimPolicy.Backoff(row.RetryCount)).UtcTicks;
        // Released, so another worker can re-grab it once the backoff window passes.
        row.ClaimedBy = null;
        row.ClaimedUntil = null;
        await session.UpdateAsync(row, ct).ConfigureAwait(false);
        LogMessageFailed(messageId, error);
    }

    /// <inheritdoc />
    public async Task<int> PurgeProcessedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = (_time.GetUtcNow() - olderThan).UtcTicks;
        // Set-based: unprocessed rows (ProcessedAt null) are never touched.
        var deleted = await session.Query<OutboxMessageRow>().Where((r, c) => r.ProcessedAt != null && r.ProcessedAt < c, cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        if (deleted > 0)
            LogPurged(deleted, boundaryName);
        return deleted;
    }

    /// <inheritdoc />
    public async Task<(int Pending, DateTimeOffset? OldestCreatedAt)> InspectPendingAsync(CancellationToken ct = default)
    {
        // Read-only: a health probe never claims rows away from the delivery pump.
        var count = await session.Query<OutboxMessageRow>().Where(r => r.ProcessedAt == null).CountAsync(ct).ConfigureAwait(false);
        if (count == 0)
            return (0, null);
        var oldest = await session.Query<OutboxMessageRow>().Where(r => r.ProcessedAt == null).MinAsync(r => r.CreatedAt, ct).ConfigureAwait(false);
        return (count, OutboxMessageRecords.Instant(oldest));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> PeekPendingAsync(int max, CancellationToken ct = default)
    {
        // Read-only: an open dashboard never holds leases.
        var rows = await session.Query<OutboxMessageRow>().Where(r => r.ProcessedAt == null).OrderBy(r => r.CreatedAt).Take(max)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(OutboxMessageRecords.ToMessage)];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} not found during {Operation}")]
    private partial void LogMessageNotFound(Guid messageId, string operation);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox message {MessageId} marked as failed: {Error}")]
    private partial void LogMessageFailed(Guid messageId, string error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Purged {Count} delivered outbox row(s) from boundary {BoundaryName}")]
    private partial void LogPurged(int count, string boundaryName);
}
