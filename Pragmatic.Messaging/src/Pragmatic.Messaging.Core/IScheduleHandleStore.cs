namespace Pragmatic.Messaging;

/// <summary>
///     Durable storage for <see cref="ScheduleHandle"/>s, making schedule cancellation
///     restart-safe on transports whose cancel needs a broker token (Azure Service Bus).
///     Optional: without it the ASB scheduler falls back to an in-process map — delivery
///     stays durable on the broker, but ids from before a restart can't be cancelled.
/// </summary>
public interface IScheduleHandleStore
{
    /// <summary>Persists the handle for a newly scheduled message.</summary>
    Task SaveAsync(ScheduleHandle handle, CancellationToken ct = default);

    /// <summary>Returns the handle, or null when unknown (already cancelled, delivered and purged, or never saved).</summary>
    Task<ScheduleHandle?> GetAsync(Guid scheduleId, CancellationToken ct = default);

    /// <summary>Removes the handle after a successful cancel (idempotent).</summary>
    Task RemoveAsync(Guid scheduleId, CancellationToken ct = default);
}
