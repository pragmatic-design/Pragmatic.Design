using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Tests.Unit.Transport;

public sealed class NullTransportTests
{
    [Fact]
    public async Task SendAsync_AlwaysReturnsSuccess()
    {
        var transport = new NullTransport();
        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com"),
            To = [new EmailAddress("recipient@example.com")],
            Subject = "Test",
            TextBody = "Body",
        };

        var result = await transport.SendAsync(message);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ReturnsMessageId()
    {
        var transport = new NullTransport();
        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com"),
            To = [new EmailAddress("recipient@example.com")],
            Subject = "Test",
            TextBody = "Body",
        };

        var result = await transport.SendAsync(message);

        result.MessageId.Should().Be(message.MessageId);
    }

    [Fact]
    public void Name_IsNull()
    {
        new NullTransport().Name.Should().Be("Null");
    }
}
