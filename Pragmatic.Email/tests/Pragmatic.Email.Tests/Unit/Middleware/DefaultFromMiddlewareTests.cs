using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Tests.Unit.Middleware;

/// <summary>
///     EmailOptions.DefaultFrom takes effect: a message without a sender goes out with it, not with
///     an empty From. The tests assert the sent message, not that the option was stored.
/// </summary>
public sealed class DefaultFromMiddlewareTests
{
    private static (IEmailSender Sender, InMemoryTransport Transport) Build(EmailAddress? defaultFrom)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddPragmaticEmail(email =>
        {
            if (defaultFrom is { } from)
                email.Configure(o => o.DefaultFrom = from);
        });

        var transport = services.AddEmailTestHarness();
        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IEmailSender>(), transport);
    }

    private static EmailMessage MessageWithoutSender() => new()
    {
        To = [new EmailAddress("recipient@example.com")],
        Subject = "No sender",
        TextBody = "Body",
    };

    [Fact]
    public async Task SendAsync_WithoutFrom_AppliesDefaultFrom()
    {
        var (sender, transport) = Build(new EmailAddress("noreply@example.com", "No Reply"));

        var result = await sender.SendAsync(MessageWithoutSender());

        result.Success.Should().BeTrue();
        transport.Sent.Should().ContainSingle();
        transport.Sent[0].Message.From.Address.Should().Be("noreply@example.com");
        transport.Sent[0].Message.From.DisplayName.Should().Be("No Reply");
    }

    [Fact]
    public async Task SendAsync_WithExplicitFrom_KeepsIt()
    {
        var (sender, transport) = Build(new EmailAddress("noreply@example.com"));

        var message = MessageWithoutSender() with { From = new EmailAddress("explicit@example.com") };
        await sender.SendAsync(message);

        transport.Sent[0].Message.From.Address.Should().Be("explicit@example.com");
    }

    [Fact]
    public async Task SendAsync_WithoutFromAndWithoutDefault_FailsWithAnActionableError()
    {
        var (sender, transport) = Build(defaultFrom: null);

        var result = await sender.SendAsync(MessageWithoutSender());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("DefaultFrom");
        result.MessageId.Should().NotBeNull("a failure must still be correlatable to the message");
        transport.Sent.Should().BeEmpty("nothing may reach the transport without a sender");
    }
}
