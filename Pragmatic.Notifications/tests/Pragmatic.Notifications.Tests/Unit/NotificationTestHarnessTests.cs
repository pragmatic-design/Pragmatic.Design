using Pragmatic.Testing.Assertions;
using Pragmatic.Notifications.Testing;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class NotificationTestHarnessTests
{
    private readonly NotificationTestHarness _harness = new();

    private static NotificationRequest CreateRequest(
        string subject = "Test",
        NotificationAudience audience = NotificationAudience.EndUser) => new()
    {
        Audience = audience,
        Recipient = NotificationRecipient.Direct("user@example.com"),
        Content = new NotificationContent { Subject = subject, Body = "Body" },
    };

    [Fact]
    public async Task SendAsync_RecordsNotification()
    {
        var result = await _harness.SendAsync(CreateRequest());

        result.Success.Should().BeTrue();
        result.NotificationId.Should().NotBeEmpty();
        _harness.Sent.Should().HaveCount(1);
        _harness.Sent[0].Synchronous.Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueAsync_RecordsNotification()
    {
        var result = await _harness.EnqueueAsync(CreateRequest());

        result.Success.Should().BeTrue();
        _harness.Sent.Should().HaveCount(1);
        _harness.Sent[0].Synchronous.Should().BeFalse();
    }

    [Fact]
    public async Task HasSentTo_ReturnsTrue_WhenAudienceMatches()
    {
        await _harness.SendAsync(CreateRequest(audience: NotificationAudience.Admin));

        _harness.HasSentTo(NotificationAudience.Admin).Should().BeTrue();
        _harness.HasSentTo(NotificationAudience.EndUser).Should().BeFalse();
    }

    [Fact]
    public async Task HasSentWithSubject_ReturnsTrue_WhenSubjectMatches()
    {
        await _harness.SendAsync(CreateRequest(subject: "Reservation Confirmed"));

        _harness.HasSentWithSubject("Reservation Confirmed").Should().BeTrue();
        _harness.HasSentWithSubject("Other").Should().BeFalse();
    }

    [Fact]
    public async Task HasSent_WithPredicate_Works()
    {
        await _harness.SendAsync(CreateRequest(subject: "Alert"));
        await _harness.SendAsync(CreateRequest(subject: "Welcome"));

        _harness.HasSent(r => r.Content.Subject == "Alert").Should().BeTrue();
        _harness.HasSent(r => r.Content.Subject == "Missing").Should().BeFalse();
    }

    [Fact]
    public async Task SentWhere_ReturnsMatchingNotifications()
    {
        await _harness.SendAsync(CreateRequest(subject: "A"));
        await _harness.SendAsync(CreateRequest(subject: "B"));
        await _harness.SendAsync(CreateRequest(subject: "A"));

        var matches = _harness.SentWhere(r => r.Content.Subject == "A");

        matches.Should().HaveCount(2);
    }

    [Fact]
    public async Task Reset_ClearsAllRecords()
    {
        await _harness.SendAsync(CreateRequest());
        await _harness.SendAsync(CreateRequest());

        _harness.Reset();

        _harness.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleSends_AreThreadSafe()
    {
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _harness.SendAsync(CreateRequest()));

        await Task.WhenAll(tasks);

        _harness.Sent.Should().HaveCount(100);
    }
}
