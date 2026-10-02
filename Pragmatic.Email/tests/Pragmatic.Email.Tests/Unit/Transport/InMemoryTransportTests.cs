using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Tests.Unit.Transport;

public sealed class InMemoryTransportTests
{
    private readonly InMemoryTransport _transport = new();

    private static EmailMessage CreateMessage(string subject = "Test", string to = "r@example.com") => new()
    {
        From = new EmailAddress("sender@example.com"),
        To = [new EmailAddress(to)],
        Subject = subject,
        TextBody = "Body",
    };

    [Fact]
    public async Task SendAsync_RecordsEmail()
    {
        await _transport.SendAsync(CreateMessage());

        _transport.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_ReturnsSuccess()
    {
        var result = await _transport.SendAsync(CreateMessage());

        result.Success.Should().BeTrue();
        result.MessageId.Should().NotBeNull();
    }

    [Fact]
    public async Task HasSentTo_ReturnsTrue_WhenAddressMatches()
    {
        await _transport.SendAsync(CreateMessage(to: "user@example.com"));

        _transport.HasSentTo("user@example.com").Should().BeTrue();
        _transport.HasSentTo("other@example.com").Should().BeFalse();
    }

    [Fact]
    public async Task HasSentWithSubject_ReturnsTrue_WhenSubjectMatches()
    {
        await _transport.SendAsync(CreateMessage(subject: "Welcome"));

        _transport.HasSentWithSubject("Welcome").Should().BeTrue();
        _transport.HasSentWithSubject("Other").Should().BeFalse();
    }

    [Fact]
    public async Task HasSent_WithPredicate_Works()
    {
        await _transport.SendAsync(CreateMessage(subject: "Alert"));

        _transport.HasSent(m => m.Subject == "Alert").Should().BeTrue();
        _transport.HasSent(m => m.Subject == "Missing").Should().BeFalse();
    }

    [Fact]
    public async Task SentWhere_ReturnsMatchingEmails()
    {
        await _transport.SendAsync(CreateMessage(subject: "A"));
        await _transport.SendAsync(CreateMessage(subject: "B"));
        await _transport.SendAsync(CreateMessage(subject: "A"));

        _transport.SentWhere(m => m.Subject == "A").Should().HaveCount(2);
    }

    [Fact]
    public async Task Reset_ClearsAll()
    {
        await _transport.SendAsync(CreateMessage());
        await _transport.SendAsync(CreateMessage());

        _transport.Reset();

        _transport.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_IsThreadSafe()
    {
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _transport.SendAsync(CreateMessage()));

        await Task.WhenAll(tasks);

        _transport.Sent.Should().HaveCount(100);
    }
}
