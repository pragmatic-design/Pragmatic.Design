namespace Pragmatic.Messaging.EFCore.Entities;

/// <summary>
///     Entity representing a processed message for idempotency deduplication.
///     Stored in <c>__IdempotencyRecords</c> table.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Unique message identifier (primary key).</summary>
    public required string MessageId { get; set; }

    /// <summary>
    ///     When this row was written — either the moment a publish was recorded, or the moment a
    ///     consumer took the claim.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The name says "processed", and that is what the purge reads it as, which holds: an old
    ///     row goes whichever state it is in. But a row can exist while the work is still running, and
    ///     for those this is when the lease started.
    /// </remarks>
    public DateTimeOffset ProcessedAt { get; set; }

    /// <summary>
    ///     When the handling finished, or <c>null</c> while it is still in progress.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The column that separates <b>somebody started</b> from <b>somebody finished</b>. Without
    ///     it a consumer that claimed the id and was then killed would leave a row every redelivery reads
    ///     as a completed one, so the message would be dropped without any handler ever running.
    /// </remarks>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When an in-progress claim may be taken over by another consumer.</summary>
    /// <remarks>
    ///     Null on a row that was never a claim — a publish recorded by the outbox is complete the
    ///     moment it is written and has nothing to lease.
    /// </remarks>
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
