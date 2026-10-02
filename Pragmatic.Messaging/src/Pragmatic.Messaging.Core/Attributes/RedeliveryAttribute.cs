using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Enables persistent delayed redelivery for a message handler: when the handler exhausts its
///     in-process <see cref="RetryAttribute"/> attempts (or fails without one), the message is
///     re-scheduled through <see cref="IMessageScheduler"/> with exponential backoff instead of
///     being dead-lettered — so the retry survives a process restart.
/// </summary>
/// <remarks>
///     <para>
///         Requires an <see cref="IMessageScheduler"/> (e.g. <c>EnableScheduledMessages()</c>, backed by
///         Pragmatic.Jobs) and an <see cref="IIdempotencyStore"/> (<c>EnableIdempotency()</c>): the
///         redelivered message keeps its original MessageId, so sibling handlers that already succeeded
///         skip it via their per-handler idempotency claim while the failed handler (whose claim is
///         released on failure) re-processes it. Without both services the handler falls back to the
///         normal failure path (throw → transport dead-letter).
///     </para>
///     <para>
///         ⚠️ <b>There are two claims on a delivery, and the coarse one is released by the republish,
///         not by the handler.</b> Besides the per-handler claim above, the bus claims the <em>bare</em>
///         MessageId before dispatching and marks it completed when the dispatch returns — and a
///         handler declaring this attribute does not throw, it schedules and returns. So the bare id is
///         left saying "handled", and the copy that comes back with the same id would be dropped at the
///         bus before any handler saw it: the message neither retried nor dead-lettered, which is worse
///         than the immediate dead-letter this attribute replaces. <c>PublishMessageJob</c> therefore
///         releases that claim before republishing, and a scheduler written by hand has to do the same.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RedeliveryAttribute : Attribute
{
    /// <summary>Maximum redeliveries before giving up (throw → dead-letter). Default: 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    ///     Base delay in seconds; redelivery n waits <c>BaseDelaySeconds * 2^n</c>. Default: 30.
    /// </summary>
    public int BaseDelaySeconds { get; set; } = 30;
}
