using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Unit;

/// <summary>
///     What the in-memory bus logs for one publish with a failing handler, as a provider receives it: id,
///     name, level, message, the state's pairs and the exception. The values are those Microsoft's generator
///     produced for these methods, so moving them to Pragmatic's call sites (<c>PragmaticLogCallSites</c>)
///     changes none of them for whoever filters, alerts or queries on them.
/// </summary>
public class TheBusKeepsItsLogEventsTests
{
    private const string MessageId = "0192f3a45b6c7d8e9f0123456789abcd";

    private sealed record OrderPlaced(int OrderId);

    private sealed class ReserveStock : IMessageHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("Stock reservation failed");
    }

    [Fact]
    public async Task APublishWithAFailingHandler_LogsAsMicrosoftsGeneratorDid()
    {
        var recorder = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        services.AddSingleton<IMessageHandler<OrderPlaced>, ReserveStock>();
        await using var provider = services.BuildServiceProvider();
        var bus = new InMemoryMessageBus(provider, provider.GetRequiredService<ILogger<InMemoryMessageBus>>(), []);

        var publish = () => bus.PublishAsync(new OrderPlaced(42), MessageContext.New() with { MessageId = MessageId });
        await publish.Should().ThrowAsync<AggregateException>();

        var entries = recorder.Entries.ToArray();

        var publishing = entries.Single(e => e.EventId.Name == "LogPublishing");
        publishing.EventId.Id.Should().Be(45204572);
        publishing.Level.Should().Be(LogLevel.Debug);
        publishing.Message.Should().Be($"Publishing message OrderPlaced (id: {MessageId})");
        publishing.Pairs.Select(p => p.Key).Should().Equal("MessageName", "MessageId", "{OriginalFormat}");
        publishing.Pairs[^1].Value.Should().Be("Publishing message {MessageName} (id: {MessageId})");

        var executing = entries.Single(e => e.EventId.Name == "LogHandlerExecuting");
        executing.EventId.Id.Should().Be(681712653);
        executing.Level.Should().Be(LogLevel.Debug);
        executing.Message.Should().Be("Executing handler ReserveStock for message OrderPlaced");

        var failed = entries.Single(e => e.EventId.Name == "LogHandlerFailed");
        failed.EventId.Id.Should().Be(399935518);
        failed.Level.Should().Be(LogLevel.Error);
        failed.Message.Should().Be("Handler ReserveStock failed for message OrderPlaced");
        failed.Exception.Should().BeOfType<InvalidOperationException>();
        failed.Exception!.Message.Should().Be("Stock reservation failed");

        var completed = entries.Single(e => e.EventId.Name == "LogPublishCompletedWithFailures");
        completed.EventId.Id.Should().Be(273968086);
        completed.Level.Should().Be(LogLevel.Warning);
        completed.Message.Should().Be("Publish completed for message OrderPlaced with 1/1 handler failure(s)");
        completed.Pairs.Select(p => p.Key).Should().Equal("MessageName", "FailureCount", "TotalCount", "{OriginalFormat}");
        completed.Pairs[1].Value.Should().BeOfType<int>();
    }
}
