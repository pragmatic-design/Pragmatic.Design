using Pragmatic.Testing.Assertions;
using Pragmatic.Notifications.Diagnostics;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class NotificationsDiagnosticsTests
{
    [Fact]
    public void SourceName_IsStableIdentifier()
    {
        NotificationsDiagnostics.SourceName.Should().Be("Pragmatic.Notifications");
    }

    [Fact]
    public void ActivitySourceAndMeter_UseSourceName()
    {
        NotificationsDiagnostics.ActivitySource.Name.Should().Be(NotificationsDiagnostics.SourceName);
        NotificationsDiagnostics.Meter.Name.Should().Be(NotificationsDiagnostics.SourceName);
    }

    [Fact]
    public void Counters_AreInitialized()
    {
        NotificationsDiagnostics.NotificationsSent.Should().NotBeNull();
        NotificationsDiagnostics.NotificationsFailed.Should().NotBeNull();
        NotificationsDiagnostics.NotificationsEnqueued.Should().NotBeNull();
        NotificationsDiagnostics.DeliveryDuration.Should().NotBeNull();
        NotificationsDiagnostics.ChannelDeliveries.Should().NotBeNull();
        NotificationsDiagnostics.ChannelFailures.Should().NotBeNull();
    }

    [Fact]
    public void Counters_HaveExpectedInstrumentNames()
    {
        NotificationsDiagnostics.NotificationsSent.Name.Should().Be("pragmatic.notifications.sent");
        NotificationsDiagnostics.NotificationsFailed.Name.Should().Be("pragmatic.notifications.failed");
        NotificationsDiagnostics.NotificationsEnqueued.Name.Should().Be("pragmatic.notifications.enqueued");
        NotificationsDiagnostics.DeliveryDuration.Name.Should().Be("pragmatic.notifications.delivery.duration");
        NotificationsDiagnostics.ChannelDeliveries.Name.Should().Be("pragmatic.notifications.channel.deliveries");
        NotificationsDiagnostics.ChannelFailures.Name.Should().Be("pragmatic.notifications.channel.failures");
    }

    [Fact]
    public void Counter_RecordsWithoutListener_DoesNotThrow()
    {
        var act = () => NotificationsDiagnostics.NotificationsSent.Add(1);

        act.Should().NotThrow();
    }
}
