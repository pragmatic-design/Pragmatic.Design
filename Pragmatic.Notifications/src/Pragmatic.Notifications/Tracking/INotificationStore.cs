namespace Pragmatic.Notifications.Tracking;

/// <summary>
///     Persistent store for notification delivery tracking.
/// </summary>
public interface INotificationStore
{
    Task<NotificationRecord> CreateAsync(NotificationRecord record, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid notificationId, DeliveryStatus status, string? errorMessage, CancellationToken ct = default);
    Task<NotificationRecord?> GetByIdAsync(Guid notificationId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRecord>> GetPendingAsync(int limit, CancellationToken ct = default);
}
