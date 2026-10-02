using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Tests.Unit;

public sealed class EmailSenderTests
{
    private static EmailMessage CreateMessage() => new()
    {
        From = new EmailAddress("sender@example.com"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = "Test",
        TextBody = "Body",
    };

    [Fact]
    public async Task SendAsync_WithEmptyPipeline_DelegatesUnchangedMessageToTransport()
    {
        var transport = new RecordingTransport();
        var sender = new EmailSender(transport, new EmailPipeline([]), NullLogger<EmailSender>.Instance);
        var message = CreateMessage();

        var result = await sender.SendAsync(message);

        result.Success.Should().BeTrue();
        transport.LastMessage.Should().BeSameAs(message);
    }

    [Fact]
    public async Task SendAsync_RunsPipelineBeforeTransport()
    {
        var transport = new RecordingTransport();
        var pipeline = new EmailPipeline([new SubjectPrefixMiddleware("[X] ")]);
        var sender = new EmailSender(transport, pipeline, NullLogger<EmailSender>.Instance);

        await sender.SendAsync(CreateMessage());

        transport.LastMessage!.Subject.Should().Be("[X] Test");
    }

    [Fact]
    public async Task SendAsync_WhenTransportSucceeds_ReturnsSuccessWithMessageId()
    {
        var transport = new RecordingTransport();
        var sender = new EmailSender(transport, new EmailPipeline([]), NullLogger<EmailSender>.Instance);
        var message = CreateMessage();

        var result = await sender.SendAsync(message);

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be(message.MessageId);
    }

    [Fact]
    public async Task SendAsync_WhenTransportFails_ReturnsFailureResult()
    {
        var transport = new FailingTransport("smtp rejected");
        var sender = new EmailSender(transport, new EmailPipeline([]), NullLogger<EmailSender>.Instance);

        var result = await sender.SendAsync(CreateMessage());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("smtp rejected");
    }

    [Fact]
    public async Task SendAsync_PropagatesCancellationTokenToTransport()
    {
        var transport = new RecordingTransport();
        var sender = new EmailSender(transport, new EmailPipeline([]), NullLogger<EmailSender>.Instance);
        using var cts = new CancellationTokenSource();

        await sender.SendAsync(CreateMessage(), cts.Token);

        transport.LastToken.Should().Be(cts.Token);
    }

    private sealed class RecordingTransport : IEmailTransport
    {
        public EmailMessage? LastMessage { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public string Name => "Recording";

        public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            LastMessage = message;
            LastToken = ct;
            return Task.FromResult(EmailResult.Succeeded(message.MessageId));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingTransport(string error) : IEmailTransport
    {
        public string Name => "Failing";

        public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
            => Task.FromResult(EmailResult.Failed(error));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SubjectPrefixMiddleware(string prefix) : IEmailMiddleware
    {
        public int Order => 0;

        public Task<EmailMessage> ProcessAsync(EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
            => next(message with { Subject = prefix + message.Subject });
    }
}
