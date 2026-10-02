using Pragmatic.MultiTenancy;
using Pragmatic.Notifications.Diagnostics;
using Pragmatic.Notifications.Delivery;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications;

/// <summary>
///     Default notification service. SendAsync delivers synchronously; EnqueueAsync writes to the BoundedChannel
///     and creates a Pending tracking record so callers can correlate the returned notificationId.
/// </summary>
internal sealed class NotificationService(
    NotificationPipeline pipeline,
    NotificationDeliveryChannel deliveryChannel,
    INotificationStore store,
    ITenantContext? tenantContext = null)
    : INotificationService
{
    public Task<NotificationResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
        => pipeline.ExecuteAsync(request, ct);

    public async Task<NotificationResult> EnqueueAsync(NotificationRequest request, CancellationToken ct = default)
    {
        // Create a Pending tracking record before enqueue so the caller can correlate the returned notificationId.
        var record = new NotificationRecord
        {
            Audience = request.Audience,
            RecipientAddress = request.Recipient.EmailAddress
                ?? request.Recipient.UserId
                ?? request.Recipient.WebhookUrl
                ?? request.Recipient.PhoneNumber
                ?? "(queued)",
            Channel = request.ChannelOverride ?? NotificationChannel.None,
            Subject = request.Content.Subject,
            Status = DeliveryStatus.Pending,
            Category = request.Category,
            Metadata = request.Metadata,
        };
        var created = await store.CreateAsync(record, ct).ConfigureAwait(false);

        // The tracking Id travels with the request so the pipeline settles this record's
        // final status (Sent/Failed) — otherwise the caller's record stays Pending forever.
        // The tenant travels with it too: delivery runs on the worker, outside this request, and
        // must resolve recipients under the tenant the notification was raised in.
        await deliveryChannel.Writer
            .WriteAsync(new QueuedNotification(request, created.Id, tenantContext?.TenantId), ct)
            .ConfigureAwait(false);

        NotificationsDiagnostics.NotificationsEnqueued.Add(1);
        return NotificationResult.Succeeded(created.Id);
    }
}
