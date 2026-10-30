using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Entities;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Messaging.Engines.Documents;

/// <summary>
///     The <see cref="IOutboxSource" /> of a boundary the documents engine serves, read through the boundary's own session: what
///     <c>EfCoreOutboxSource</c> does on a <c>DbContext</c>, under <see cref="OutboxClaimPolicy" />'s rules. The pending documents' keys are read
///     (oldest first, past their backoff, a batch), claimed with one conditional update (pending, and unclaimed or past its lease: another worker's
///     claim stands), and the documents that carry this poll's token returned.
/// </summary>
/// <remarks>
///     On Cosmos DB, which updates a document by its id only, the claim is the engine's read-then-patch at each document's etag: a document
///     another worker claimed in between is read again, and no longer selected.
/// </remarks>
public sealed partial class DocOutboxSource(DocSession session, string boundaryName, ILogger<DocOutboxSource> logger, TimeProvider? time = null) : IOutboxSource
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public string BoundaryName => boundaryName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcTicks;
        var token = OutboxClaimPolicy.NewClaimToken();

        // The keys first, then the claim by key: an update by filter writes every document it selects, so the batch is chosen before it.
        var candidates = await session.Query<OutboxMessageDocument>()
            .Where((d, n) => d.ProcessedAt == null && (d.NextAttemptAt == null || d.NextAttemptAt <= n)
                && (d.ClaimedBy == null || d.ClaimedUntil == null || d.ClaimedUntil < n), now)
            .OrderBy(d => d.CreatedAt).Take(batchSize).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return [];

        var claimed = await session.Query<OutboxMessageDocument>()
            .Where((d, a) => a.Ids.Contains(d.Id) && d.ProcessedAt == null && (d.ClaimedBy == null || d.ClaimedUntil == null || d.ClaimedUntil < a.Now),
                (Ids: candidates, Now: now))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.ClaimedBy, a.Token).Set(d => d.ClaimedUntil, a.Until),
                (Token: token, Until: (long?)(now + OutboxClaimPolicy.ClaimLease.Ticks)), ct).ConfigureAwait(false);
        if (claimed == 0)
            return [];

        var documents = await session.Query<OutboxMessageDocument>().Where((d, t) => d.ClaimedBy == t && d.ProcessedAt == null, token)
            .OrderBy(d => d.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return [.. documents.Select(OutboxMessageRecords.ToMessage)];
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default)
    {
        var affected = await session.Query<OutboxMessageDocument>().Where((d, id) => d.Id == id, messageId)
            .ExecuteUpdateAsync((s, at) => s.Set(d => d.ProcessedAt, at), (long?)_time.GetUtcNow().UtcTicks, ct).ConfigureAwait(false);
        if (affected == 0)
            LogMessageNotFound(messageId, "MarkProcessed");
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        var document = await session.Query<OutboxMessageDocument>().FirstOrDefaultAsync((d, id) => d.Id == id, messageId, ct).ConfigureAwait(false);
        if (document is null)
        {
            LogMessageNotFound(messageId, "MarkFailed");
            return;
        }

        document.RetryCount++;
        document.Error = error;
        document.NextAttemptAt = (_time.GetUtcNow() + OutboxClaimPolicy.Backoff(document.RetryCount)).UtcTicks;
        // Released, so another worker can re-grab it once the backoff window passes.
        document.ClaimedBy = null;
        document.ClaimedUntil = null;
        await session.ReplaceAsync(document, ct).ConfigureAwait(false);
        LogMessageFailed(messageId, error);
    }

    /// <inheritdoc />
    public async Task<int> PurgeProcessedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = (_time.GetUtcNow() - olderThan).UtcTicks;
        // Set-based: unprocessed documents (ProcessedAt null) are never touched.
        var deleted = (int)await session.Query<OutboxMessageDocument>().Where((d, c) => d.ProcessedAt != null && d.ProcessedAt < c, cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        if (deleted > 0)
            LogPurged(deleted, boundaryName);
        return deleted;
    }

    /// <inheritdoc />
    public async Task<(int Pending, DateTimeOffset? OldestCreatedAt)> InspectPendingAsync(CancellationToken ct = default)
    {
        // Read-only: a health probe never claims documents away from the delivery pump.
        var count = (int)await session.Query<OutboxMessageDocument>().Where(d => d.ProcessedAt == null).CountAsync(ct).ConfigureAwait(false);
        if (count == 0)
            return (0, null);
        var oldest = await session.Query<OutboxMessageDocument>().Where(d => d.ProcessedAt == null).MinAsync(d => d.CreatedAt, ct).ConfigureAwait(false);
        return (count, OutboxMessageRecords.Instant(oldest));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> PeekPendingAsync(int max, CancellationToken ct = default)
    {
        // Read-only: an open dashboard never holds leases.
        var documents = await session.Query<OutboxMessageDocument>().Where(d => d.ProcessedAt == null).OrderBy(d => d.CreatedAt).Take(max)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. documents.Select(OutboxMessageRecords.ToMessage)];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} not found during {Operation}")]
    private partial void LogMessageNotFound(Guid messageId, string operation);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox message {MessageId} marked as failed: {Error}")]
    private partial void LogMessageFailed(Guid messageId, string error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Purged {Count} delivered outbox document(s) from boundary {BoundaryName}")]
    private partial void LogPurged(int count, string boundaryName);
}
