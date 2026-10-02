namespace Pragmatic.Notifications;

/// <summary>
///     Delivery channel for notifications. Flags enum — a notification can target multiple channels.
/// </summary>
[Flags]
public enum NotificationChannel
{
    None = 0,
    Email = 1 << 0,
    Sms = 1 << 1,
    Push = 1 << 2,
    InApp = 1 << 3,
    Webhook = 1 << 4,
    Slack = 1 << 5,
}
