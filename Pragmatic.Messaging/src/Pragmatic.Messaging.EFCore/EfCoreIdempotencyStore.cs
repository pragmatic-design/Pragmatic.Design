using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.EFCore.Entities;

namespace Pragmatic.Messaging.EFCore;

/// <summary>
///     EF Core-backed idempotency store for exactly-once message processing.
///     Persists processed message IDs in <see cref="IdempotencyRecord"/> rows.
/// </summary>
public sealed partial class EfCoreIdempotencyStore(
    DbContext dbContext,
    ILogger<EfCoreIdempotencyStore> logger) : IIdempotencyStore
{
    /// <inheritdoc />
    public async Task<bool> TryMarkAsProcessedAsync(string messageId, CancellationToken ct = default)
    {
        var exists = await dbContext.Set<IdempotencyRecord>()
            .AnyAsync(r => r.MessageId == messageId, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            LogDuplicateDetected(messageId);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var record = new IdempotencyRecord
        {
            MessageId = messageId,
            ProcessedAt = now,
            // Complete the moment it is written: this path records work that has already happened —
            // the outbox calls it after a successful publish. A claim taken before the work uses
            // TryClaimAsync, which leaves CompletedAt null until it finishes.
            CompletedAt = now,
        };

        dbContext.Set<IdempotencyRecord>().Add(record);

        try
        {
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            // Distinguish a genuine concurrent duplicate (unique-key violation on MessageId) from a
            // transient DB failure. Treating every DbUpdateException as "duplicate" would return false
            // on a transient error, making the delivery path mark the message processed and silently
            // drop it. Detach the failed insert and re-probe: if the row now exists it was a real
            // duplicate (false); otherwise the failure was transient — rethrow so delivery retries.
            dbContext.Entry(record).State = EntityState.Detached;
            var nowExists = await dbContext.Set<IdempotencyRecord>()
                .AnyAsync(r => r.MessageId == messageId, ct)
                .ConfigureAwait(false);
            if (nowExists)
            {
                LogConcurrentDuplicate(messageId);
                return false;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MessageClaim> TryClaimAsync(
        string messageId, TimeSpan lease, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now + lease;

        var existing = await dbContext.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(r => r.MessageId == messageId, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.CompletedAt is not null)
            {
                LogDuplicateDetected(messageId);
                return MessageClaim.AlreadyHandled;
            }

            // In progress. Only a lease that has run out may be taken over — the holder is either
            // still working or gone, and the lease is the only thing that tells those apart.
            if (existing.LeaseExpiresAt is { } held && held > now)
                return MessageClaim.HeldByAnother;

            existing.ProcessedAt = now;
            existing.LeaseExpiresAt = expires;
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            return MessageClaim.Claimed;
        }

        var record = new IdempotencyRecord
        {
            MessageId = messageId,
            ProcessedAt = now,
            CompletedAt = null,
            LeaseExpiresAt = expires,
        };

        dbContext.Set<IdempotencyRecord>().Add(record);

        try
        {
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            return MessageClaim.Claimed;
        }
        catch (DbUpdateException)
        {
            // The same reasoning as TryMarkAsProcessedAsync: a unique-key violation means somebody
            // inserted between the read and the write, and anything else is transient and must not be
            // reported as a duplicate. Detach and re-probe rather than guess.
            dbContext.Entry(record).State = EntityState.Detached;
            var winner = await dbContext.Set<IdempotencyRecord>()
                .FirstOrDefaultAsync(r => r.MessageId == messageId, ct)
                .ConfigureAwait(false);

            if (winner is null) throw;

            LogConcurrentDuplicate(messageId);
            return winner.CompletedAt is not null ? MessageClaim.AlreadyHandled : MessageClaim.HeldByAnother;
        }
    }

    /// <inheritdoc />
    public async Task MarkClaimCompletedAsync(string messageId, CancellationToken ct = default)
    {
        await dbContext.Set<IdempotencyRecord>()
            .Where(r => r.MessageId == messageId)
            .ExecuteUpdateAsync(
                r => r.SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow)
                      .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
        // ⚠️ Completed, not merely present. A row can exist while the work is still running, and
        // reading that as "done" is the defect this column exists to prevent. The two key
        // spaces do not overlap — the outbox prefixes its ids — but a reader that says
        // "processed" has to mean it whoever wrote the row.
        => await dbContext.Set<IdempotencyRecord>()
            .AnyAsync(r => r.MessageId == messageId && r.CompletedAt != null, ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task RemoveAsync(string messageId, CancellationToken ct = default)
    {
        await dbContext.Set<IdempotencyRecord>()
            .Where(r => r.MessageId == messageId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - age;

        var deleted = await dbContext.Set<IdempotencyRecord>()
            .Where(r => r.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deleted > 0)
            LogPurged(deleted, age);
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Duplicate message detected: {MessageId}")]
    private partial void LogDuplicateDetected(string messageId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Concurrent duplicate insert for message: {MessageId}")]
    private partial void LogConcurrentDuplicate(string messageId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} idempotency records older than {Age}")]
    private partial void LogPurged(int count, TimeSpan age);
}
