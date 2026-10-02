namespace Pragmatic.Notifications;

/// <summary>
///     Entry point for sending notifications. Supports synchronous delivery and fire-and-forget enqueue.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface INotificationService
{
    /// <summary>
    ///     Sends a notification synchronously — waits for delivery result from the provider.
    /// </summary>
    Task<NotificationResult> SendAsync(NotificationRequest request, CancellationToken ct = default);

    /// <summary>
    ///     Enqueues a notification for background delivery — returns immediately with a tracking ID.
    /// </summary>
    Task<NotificationResult> EnqueueAsync(NotificationRequest request, CancellationToken ct = default);
}
