using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Testing;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Tests.Unit.Messages;

public sealed class EmailServiceTests
{
    [Fact]
    public void AddPragmaticEmail_RegistersIEmailSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail();

        var sp = services.BuildServiceProvider();

        sp.GetService<IEmailSender>().Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticEmail_DefaultTransport_IsNull()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail();

        var sp = services.BuildServiceProvider();
        var transport = sp.GetRequiredService<IEmailTransport>();

        transport.Name.Should().Be("Null");
    }

    [Fact]
    public void AddEmailTestHarness_ReplacesTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail();
        var harness = services.AddEmailTestHarness();

        var sp = services.BuildServiceProvider();
        var transport = sp.GetRequiredService<IEmailTransport>();

        transport.Should().BeSameAs(harness);
    }

    [Fact]
    public async Task EmailSender_DelegatesToTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail();
        var harness = services.AddEmailTestHarness();

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<IEmailSender>();

        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com"),
            To = [new EmailAddress("recipient@example.com")],
            Subject = "Test",
            TextBody = "Body",
        };

        var result = await sender.SendAsync(message);

        result.Success.Should().BeTrue();
        harness.Sent.Should().HaveCount(1);
    }
}
