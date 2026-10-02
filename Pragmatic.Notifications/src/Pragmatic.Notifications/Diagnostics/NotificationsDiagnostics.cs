using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Notifications.Diagnostics;

/// <summary>
///     OpenTelemetry diagnostics for Pragmatic.Notifications.
/// </summary>
public static class NotificationsDiagnostics
{
    public const string SourceName = "Pragmatic.Notifications";

    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    public static readonly Counter<long> NotificationsSent = Meter.CreateCounter<long>("pragmatic.notifications.sent", "notifications");
    public static readonly Counter<long> NotificationsFailed = Meter.CreateCounter<long>("pragmatic.notifications.failed", "notifications");
    public static readonly Counter<long> NotificationsEnqueued = Meter.CreateCounter<long>("pragmatic.notifications.enqueued", "notifications");
    public static readonly Histogram<double> DeliveryDuration = Meter.CreateHistogram<double>("pragmatic.notifications.delivery.duration", "ms");
    public static readonly Counter<long> ChannelDeliveries = Meter.CreateCounter<long>("pragmatic.notifications.channel.deliveries", "deliveries", "Per-channel delivery count");
    public static readonly Counter<long> ChannelFailures = Meter.CreateCounter<long>("pragmatic.notifications.channel.failures", "failures", "Per-channel failure count");
}
