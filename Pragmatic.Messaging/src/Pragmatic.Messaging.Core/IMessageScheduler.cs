namespace Pragmatic.Messaging;

/// <summary>
///     Schedules messages for future delivery.
///     Implementations use a backing job scheduler (e.g., Pragmatic.Jobs)
///     to persist and execute the publish at the scheduled time.
/// </summary>
public interface IMessageScheduler
{
    /// <summary>Schedules a message for delivery after a delay.</summary>
    /// <returns>The schedule identifier (can be used to cancel).</returns>
    Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default)
        where T : notnull;

    /// <summary>Schedules a message for delivery at a specific time.</summary>
    /// <returns>The schedule identifier (can be used to cancel).</returns>
    Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt, CancellationToken ct = default)
        where T : notnull;

    /// <summary>
    ///     Schedules a message for delivery after a delay, preserving the message context
    ///     (MessageId, RetryCount, tenant/user, correlation) across the schedule boundary.
    ///     Used by SG-generated redelivery: keeping the original MessageId lets idempotent
    ///     sibling handlers skip the redelivered message. Default implementation ignores the
    ///     context (implementations should override to carry it).
    /// </summary>
    Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, MessageContext context, CancellationToken ct = default)
        where T : notnull
        => ScheduleAsync(message, delay, ct);

    /// <summary>Cancels a previously scheduled message.</summary>
    Task CancelAsync(Guid scheduleId, CancellationToken ct = default);
}
