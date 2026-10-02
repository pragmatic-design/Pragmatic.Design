namespace Pragmatic.Notifications.Configuration;

/// <summary>
///     Global notification options.
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>
    ///     Capacity of the background delivery channel used by <c>EnqueueAsync</c>. When it is full,
    ///     enqueuing waits rather than dropping notifications.
    /// </summary>
    public int DeliveryChannelCapacity { get; set; } = 1000;

    // No sender name or address here. Sender identity belongs to the channel that has one:
    // SmtpOptions.SenderAddress/SenderName for e-mail (with EmailOptions.DefaultFrom as the library-wide fallback) and FromNumber for SMS.
    // Slack and webhook have no sender at all. A third place to configure it would only be a way to
    // set a value that quietly does nothing.
}
