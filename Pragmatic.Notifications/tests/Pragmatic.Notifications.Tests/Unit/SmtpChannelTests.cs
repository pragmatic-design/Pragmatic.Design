using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Email;
using Pragmatic.Notifications.Email;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class SmtpChannelTests
{
    private static readonly IOptions<SmtpOptions> DefaultOptions =
        Options.Create(new SmtpOptions { SenderAddress = "noreply@example.com", SenderName = "Pragmatic" });

    private static ResolvedRecipient Recipient(string address = "user@example.com")
        => new(address, NotificationChannel.Email, null, null, null);

    private static NotificationContent Content(string? body = "Body", string? html = null)
        => new() { Subject = "Subject", Body = body!, HtmlBody = html };

    private static SmtpChannel CreateChannel(IEmailSender sender)
        => new(sender, DefaultOptions, NullLogger<SmtpChannel>.Instance);

    [Fact]
    public void Channel_ReturnsEmail()
    {
        var sender = new EmailSenderMock();

        var channel = CreateChannel(sender);

        channel.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task DeliverAsync_WhenSenderSucceeds_ReturnsSucceededWithProviderId()
    {
        var sender = new EmailSenderMock();
        sender.SendAsync.Returns(EmailResult.Succeeded("msg-123"));
        var channel = CreateChannel(sender);

        var result = await channel.DeliverAsync(Recipient(), Content());

        result.Success.Should().BeTrue();
        result.ProviderId.Should().Be("msg-123");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeliverAsync_WhenSenderFails_ReturnsFailedWithError()
    {
        var sender = new EmailSenderMock();
        sender.SendAsync.Returns(EmailResult.Failed("mailbox full"));
        var channel = CreateChannel(sender);

        var result = await channel.DeliverAsync(Recipient(), Content());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("mailbox full");
    }

    [Fact]
    public async Task DeliverAsync_WhenSenderThrows_ReturnsFailedWithExceptionMessage()
    {
        var sender = new EmailSenderMock();
        sender.SendAsync.Throws(new InvalidOperationException("transport down"));
        var channel = CreateChannel(sender);

        var result = await channel.DeliverAsync(Recipient(), Content());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("transport down");
    }

    [Fact]
    public async Task DeliverAsync_InvokesEmailSenderOnce()
    {
        var sender = new EmailSenderMock();
        sender.SendAsync.Returns(EmailResult.Succeeded("id"));
        var channel = CreateChannel(sender);

        await channel.DeliverAsync(Recipient("dest@example.com"), Content());

        sender.SendAsync.Received(1);
    }

    [Fact]
    public async Task DeliverAsync_WithHtmlBody_StillDelegatesToSenderAndSucceeds()
    {
        var sender = new EmailSenderMock();
        sender.SendAsync.Returns(EmailResult.Succeeded("id"));
        var channel = CreateChannel(sender);

        var result = await channel.DeliverAsync(Recipient(), Content(body: "plain", html: "<p>rich</p>"));

        result.Success.Should().BeTrue();
        sender.SendAsync.Received(1);
    }
}
