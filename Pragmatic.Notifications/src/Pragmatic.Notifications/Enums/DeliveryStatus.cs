namespace Pragmatic.Notifications;

/// <summary>
///     Lifecycle status of a notification delivery attempt.
/// </summary>
public enum DeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    Read = 3,
    Failed = 4,
    Bounced = 5,
}
