namespace Pragmatic.Messaging;

/// <summary>
///     Deduplication store for exactly-once message processing.
///     Called by transport consumers before dispatching to handlers.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    ///     Attempts to mark a message as processed.
    ///     Returns true if the message has NOT been seen before (and marks it).
    ///     Returns false if the message was already processed (duplicate).
    /// </summary>
    Task<bool> TryMarkAsProcessedAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    ///     Whether an id has already been marked, without marking it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Added because <see cref="TryMarkAsProcessedAsync" /> answers "was it seen?" only by
    ///     claiming it, which conflates <b>being attempted</b> with <b>having succeeded</b>. The outbox
    ///     claimed before publishing and a process that died in between came back, read the claim as a
    ///     delivery, and marked the row processed without ever publishing it — the message gone and the
    ///     row saying otherwise. Reading and claiming had to be separable for the claim to
    ///     be written where it is true: after the publish.
    /// </remarks>
    Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    ///     Claims a message for handling, for at most <paramref name="lease" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For the <b>consume</b> side, where the claim must come before the work: a handler that
    ///         has already run must not run again, so the claim cannot be moved after it the way the
    ///         outbox's was. What it gains instead is a state and a lease — a claim that is
    ///         in progress is not the same as one that finished, and one whose holder died is not the
    ///         same as one whose holder is still working.
    ///     </para>
    ///     <para>
    ///         ⚠️ Finish with <see cref="MarkClaimCompletedAsync" /> on success or
    ///         <see cref="RemoveAsync" /> on failure. A claim left in progress is only taken over once
    ///         its lease runs out, so the lease is how long a message waits after a process is killed —
    ///         not a timeout on the handler, which keeps running.
    ///     </para>
    /// </remarks>
    Task<MessageClaim> TryClaimAsync(string messageId, TimeSpan lease, CancellationToken ct = default);

    /// <summary>
    ///     Marks a claim taken by <see cref="TryClaimAsync" /> as finished, so redeliveries are dropped
    ///     rather than taken over when the lease expires.
    /// </summary>
    Task MarkClaimCompletedAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    ///     Releases a claim made by <see cref="TryMarkAsProcessedAsync"/>. Called when delivery/handling
    ///     FAILED after the id was claimed, so a retry/redelivery can re-attempt instead of the message
    ///     being treated as an already-processed duplicate and silently dropped. Idempotent if the id is
    ///     not present.
    /// </summary>
    Task RemoveAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    ///     Removes entries older than the specified age.
    /// </summary>
    Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default);
}
