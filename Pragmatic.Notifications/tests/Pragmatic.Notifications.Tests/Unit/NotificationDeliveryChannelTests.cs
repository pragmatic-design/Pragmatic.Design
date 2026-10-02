using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Notifications.Configuration;
using Pragmatic.Notifications.Delivery;

namespace Pragmatic.Notifications.Tests.Unit;

// Uses only non-blocking TryWrite/TryRead to assert capacity and overflow semantics;
// blocking ReadAsync/WriteAsync are intentionally avoided so a misconfigured channel cannot deadlock the suite.
public sealed class NotificationDeliveryChannelTests
{
    private static NotificationDeliveryChannel Create(int capacity = 1000)
        => new(Options.Create(new NotificationOptions { DeliveryChannelCapacity = capacity }));

    private static QueuedNotification Queued(string subject = "S") => new(
        new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct("user@example.com"),
            Content = new NotificationContent { Subject = subject, Body = "B" },
        },
        Guid.CreateVersion7(),
        TenantId: null);

    [Fact]
    public void TryWriteThenTryRead_RoundTripsSameItem()
    {
        var sut = Create();
        var queued = Queued("hello");

        sut.Writer.TryWrite(queued).Should().BeTrue();

        sut.Reader.TryRead(out var read).Should().BeTrue();
        read.Request.Should().BeSameAs(queued.Request);
        read.TrackingId.Should().Be(queued.TrackingId);
    }

    [Fact]
    public void Writer_AcceptsItemsUpToCapacity()
    {
        var sut = Create(capacity: 2);

        sut.Writer.TryWrite(Queued("1")).Should().BeTrue();
        sut.Writer.TryWrite(Queued("2")).Should().BeTrue();

        sut.Reader.TryRead(out var first).Should().BeTrue();
        first.Request.Content.Subject.Should().Be("1");
        sut.Reader.TryRead(out var second).Should().BeTrue();
        second.Request.Content.Subject.Should().Be("2");
    }

    [Fact]
    public void Writer_WhenFull_RejectsWriteWithoutDropping()
    {
        var sut = Create(capacity: 1);

        sut.Writer.TryWrite(Queued("first")).Should().BeTrue();
        // Wait mode: a full channel back-pressures instead of silently evicting the oldest
        // item (which would orphan its Pending tracking record forever).
        sut.Writer.TryWrite(Queued("overflow")).Should().BeFalse();

        sut.Reader.TryRead(out var remaining).Should().BeTrue();
        remaining.Request.Content.Subject.Should().Be("first");
        sut.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Reader_WhenEmpty_TryReadReturnsFalse()
    {
        var sut = Create();

        sut.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Channel_CapacityIsDrivenFromOptions_AllowsConfiguredCount()
    {
        var sut = Create(capacity: 3);

        sut.Writer.TryWrite(Queued("a")).Should().BeTrue();
        sut.Writer.TryWrite(Queued("b")).Should().BeTrue();
        sut.Writer.TryWrite(Queued("c")).Should().BeTrue();

        // All three configured slots accepted without dropping.
        var subjects = new List<string>();
        while (sut.Reader.TryRead(out var item))
            subjects.Add(item.Request.Content.Subject);

        subjects.Should().Equal("a", "b", "c");
    }
}
