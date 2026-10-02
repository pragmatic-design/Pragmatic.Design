namespace Pragmatic.Notifications.Channels;

/// <summary>
///     Delivers a notification to a single recipient via a specific channel (email, webhook, SMS, etc.).
/// </summary>
public interface INotificationChannel
{
    /// <summary>The channel type this provider handles.</summary>
    NotificationChannel Channel { get; }

    /// <summary>Delivers the notification content to the resolved recipient.</summary>
    Task<DeliveryResult> DeliverAsync(ResolvedRecipient recipient, NotificationContent content, CancellationToken ct = default);
}
