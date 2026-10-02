using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Middleware;

namespace Pragmatic.Email.Tests.Unit.Middleware;

public sealed class EmailPipelineTests
{
    private static EmailMessage CreateMessage() => new()
    {
        From = new EmailAddress("sender@example.com"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = "Test",
        TextBody = "Body",
    };

    [Fact]
    public async Task Execute_EmptyPipeline_ReturnsOriginalMessage()
    {
        var pipeline = new EmailPipeline([]);
        var message = CreateMessage();

        var result = await pipeline.ExecuteAsync(message, CancellationToken.None);

        result.Should().BeSameAs(message);
    }

    [Fact]
    public async Task Execute_SingleMiddleware_TransformsMessage()
    {
        var middleware = new SubjectPrefixMiddleware("[TEST] ");
        var pipeline = new EmailPipeline([middleware]);
        var message = CreateMessage();

        var result = await pipeline.ExecuteAsync(message, CancellationToken.None);

        result.Subject.Should().Be("[TEST] Test");
    }

    [Fact]
    public async Task Execute_MultipleMiddleware_ExecuteInOrder()
    {
        var first = new SubjectPrefixMiddleware("A-", order: 1);
        var second = new SubjectPrefixMiddleware("B-", order: 2);
        var pipeline = new EmailPipeline([second, first]); // Unordered input

        var result = await pipeline.ExecuteAsync(CreateMessage(), CancellationToken.None);

        result.Subject.Should().Be("B-A-Test"); // first runs first, then second
    }

    [Fact]
    public async Task Execute_MiddlewareCanShortCircuit()
    {
        var blocker = new BlockingMiddleware();
        var never = new SubjectPrefixMiddleware("NEVER-");
        var pipeline = new EmailPipeline([blocker, never]);

        var result = await pipeline.ExecuteAsync(CreateMessage(), CancellationToken.None);

        result.Subject.Should().Be("BLOCKED");
        result.Subject.Should().NotContain("NEVER");
    }

    [Fact]
    public async Task Execute_NextPassesModifiedMessage()
    {
        var addsHeader = new HeaderMiddleware("X-Step", "1");
        var checksHeader = new HeaderCheckMiddleware("X-Step");
        var pipeline = new EmailPipeline([addsHeader, checksHeader]);

        var result = await pipeline.ExecuteAsync(CreateMessage(), CancellationToken.None);

        result.Headers.Should().ContainKey("X-Verified");
    }

    // Test middleware implementations

    private sealed class SubjectPrefixMiddleware(string prefix, int order = 0) : IEmailMiddleware
    {
        public int Order => order;

        public Task<EmailMessage> ProcessAsync(EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
            => next(message with { Subject = prefix + message.Subject });
    }

    private sealed class BlockingMiddleware : IEmailMiddleware
    {
        public int Order => -1;

        public Task<EmailMessage> ProcessAsync(EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
            => Task.FromResult(message with { Subject = "BLOCKED" }); // Does NOT call next
    }

    private sealed class HeaderMiddleware(string name, string value) : IEmailMiddleware
    {
        public int Order => 0;

        public Task<EmailMessage> ProcessAsync(EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
        {
            var headers = new Dictionary<string, string>(message.Headers) { [name] = value };
            return next(message with { Headers = headers });
        }
    }

    private sealed class HeaderCheckMiddleware(string expectedHeader) : IEmailMiddleware
    {
        public int Order => 1;

        public Task<EmailMessage> ProcessAsync(EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
        {
            if (message.Headers.ContainsKey(expectedHeader))
            {
                var headers = new Dictionary<string, string>(message.Headers) { ["X-Verified"] = "true" };
                return next(message with { Headers = headers });
            }

            return next(message);
        }
    }
}
