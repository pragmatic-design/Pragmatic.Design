using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Diagnostics;

namespace Pragmatic.Messaging.Tests.Unit;

public class MessagingDiagnosticsTests
{
    [Fact]
    public void ActivitySource_HasExpectedName()
    {
        MessagingDiagnostics.ActivitySource.Name.Should().Be(MessagingDiagnostics.SourceName);
        MessagingDiagnostics.SourceName.Should().Be("Pragmatic.Messaging");
    }

    [Fact]
    public void Meter_HasExpectedName()
    {
        MessagingDiagnostics.Meter.Name.Should().Be(MessagingDiagnostics.SourceName);
        MessagingDiagnostics.SourceName.Should().Be("Pragmatic.Messaging");
    }

    [Fact]
    public void StartActivity_WithNoListener_ReturnsNull()
    {
        // No ActivityListener registered → ActivitySource produces no Activity.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("Publish.OrderPlaced");

        Assert.Null(activity);
    }

    [Fact]
    public void StartActivity_WithListener_ProducesActivityNamingTheMessageType()
    {
        using var listener = CreateAllDataListener();

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("Publish.OrderPlaced");

        Assert.NotNull(activity);
        activity.Source.Should().BeSameAs(MessagingDiagnostics.ActivitySource);
        activity.DisplayName.Should().Be("Publish.OrderPlaced");
    }

    [Fact]
    public void Counters_AreNonNullAndCreatedOnTheSharedMeter()
    {
        MessagingDiagnostics.MessagesPublished.Should().NotBeNull();
        MessagingDiagnostics.HandlerFailures.Should().NotBeNull();
        MessagingDiagnostics.HandlerDuration.Should().NotBeNull();
        MessagingDiagnostics.DeadLettered.Should().NotBeNull();
        MessagingDiagnostics.MessagesPublished.Meter.Should().BeSameAs(MessagingDiagnostics.Meter);
        MessagingDiagnostics.HandlerDuration.Meter.Should().BeSameAs(MessagingDiagnostics.Meter);
    }

    private static ActivityListener CreateAllDataListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingDiagnostics.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
