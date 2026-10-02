using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.EFCore;

/// <summary>
///     EF Core-backed implementation of <see cref="IOutboxSource"/>.
///     Queries and updates <see cref="OutboxMessage"/> rows in the given DbContext.
/// </summary>
public sealed partial class EfCoreOutboxSource(
    DbContext dbContext,
    string boundaryName,
    ILogger<EfCoreOutboxSource> logger) : IOutboxSource
{
    /// <summary>How long a claim is held before another worker may re-grab the row (crash recovery).</summary>
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public string BoundaryName => boundaryName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var claimedUntil = now + ClaimLease;

        // Unique per-poll token: stamp eligible rows with it, then select only what we won.
        // This makes claim+select atomic across replicas — a plain Where(...).Take() lets every
        // replica grab the same rows and deliver each N times.
        var claimToken = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        // A row is eligible when: unprocessed, past its backoff window, and either unclaimed
        // or its claim has expired (so a crashed worker's rows are re-grabbable).
        // Select candidate ids first (Take is not translatable inside ExecuteUpdate on every
        // provider, e.g. SQLite), then claim them with a CAS guard.
        var candidateIds = await dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null
                && (m.NextAttemptAt == null || m.NextAttemptAt <= now)
                && (m.ClaimedBy == null || m.ClaimedUntil == null || m.ClaimedUntil < now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidateIds.Count == 0)
            return [];

        // Atomic claim CAS: the eligibility predicate is re-checked inside the UPDATE, so a
        // concurrent replica that claimed a candidate between the select and here is excluded.
        var claimed = await dbContext.Set<OutboxMessage>()
            .Where(m => candidateIds.Contains(m.Id)
                && m.ProcessedAt == null
                && (m.ClaimedBy == null || m.ClaimedUntil == null || m.ClaimedUntil < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ClaimedBy, claimToken)
                .SetProperty(m => m.ClaimedUntil, claimedUntil), ct)
            .ConfigureAwait(false);

        if (claimed == 0)
            return [];

        return await dbContext.Set<OutboxMessage>()
            .Where(m => m.ClaimedBy == claimToken && m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default)
    {
        var affected = await dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedAt, DateTimeOffset.UtcNow), ct)
            .ConfigureAwait(false);

        if (affected == 0)
            LogMessageNotFound(messageId, "MarkProcessed");
    }

    // Backoff is capped so a long-failing message still gets retried at a sane cadence
    // (and never overflows when shifting by a large RetryCount).
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Load to compute exponential backoff from the (incremented) retry count.
        var message = await dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .Select(m => new { m.RetryCount })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (message is null)
        {
            LogMessageNotFound(messageId, "MarkFailed");
            return;
        }

        var nextAttemptAt = now + ComputeBackoff(message.RetryCount + 1);

        await dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Error, error)
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                // Release the claim so another worker can re-grab it once the backoff window passes.
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimedUntil, (DateTimeOffset?)null), ct)
            .ConfigureAwait(false);

        LogMessageFailed(messageId, error);
    }

    /// <inheritdoc />
    public async Task<int> PurgeProcessedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;

        // Set-based delete of delivered rows past the retention window — no rows are loaded into
        // the change tracker. Unprocessed rows (ProcessedAt == null) are never touched.
        var deleted = await dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deleted > 0)
            LogPurged(deleted, boundaryName);

        return deleted;
    }

    /// <inheritdoc />
    public async Task<(int Pending, DateTimeOffset? OldestCreatedAt)> InspectPendingAsync(CancellationToken ct = default)
    {
        // Read-only count + oldest — no ExecuteUpdate, so a health probe never claims rows away
        // from the delivery pump.
        var pending = dbContext.Set<OutboxMessage>().Where(m => m.ProcessedAt == null);
        var count = await pending.CountAsync(ct).ConfigureAwait(false);
        var oldest = count == 0
            ? null
            : await pending.MinAsync(m => (DateTimeOffset?)m.CreatedAt, ct).ConfigureAwait(false);
        return (count, oldest);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> PeekPendingAsync(int max, CancellationToken ct = default)
    {
        // Read-only snapshot for the dashboard — AsNoTracking, no ExecuteUpdate claim/lease, so an open
        // dashboard never steals rows from the delivery pump (mirror of InspectPendingAsync's intent).
        return await dbContext.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>Exponential backoff: 2^attempt seconds, capped at <see cref="MaxBackoff"/>.</summary>
    private static TimeSpan ComputeBackoff(int attempt)
    {
        // Cap the exponent before shifting to avoid overflow on a long-failing message.
        var exponent = Math.Min(attempt, 16);
        var seconds = Math.Min(Math.Pow(2, exponent), MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} not found during {Operation}")]
    private partial void LogMessageNotFound(Guid messageId, string operation);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Outbox message {MessageId} marked as failed: {Error}")]
    private partial void LogMessageFailed(Guid messageId, string error);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Purged {Count} delivered outbox row(s) from boundary {BoundaryName}")]
    private partial void LogPurged(int count, string boundaryName);
}
