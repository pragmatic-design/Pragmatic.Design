using Pragmatic.Testing.Assertions;
using Pragmatic.Events;

namespace Pragmatic.Messaging.Tests.Unit;

public class MessageHandlerEventAdapterTests
{
    public sealed record SampleEvent(string Value) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class RecordingHandler(int order, List<int> tracker) : IMessageHandler<SampleEvent>
    {
        public List<SampleEvent> Received { get; } = [];
        public int Order => order;

        public Task HandleAsync(SampleEvent message, MessageContext context, CancellationToken ct = default)
        {
            tracker.Add(order);
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ContextCapturingHandler : IMessageHandler<SampleEvent>
    {
        public MessageContext? Captured { get; private set; }

        public Task HandleAsync(SampleEvent message, MessageContext context, CancellationToken ct = default)
        {
            Captured = context;
            return Task.CompletedTask;
        }
    }

    private static MessageHandlerEventAdapter<SampleEvent> CreateAdapter(IEnumerable<IMessageHandler<SampleEvent>> handlers)
        => new(handlers);

    [Fact]
    public async Task HandleAsync_WithSingleHandler_ForwardsEventToHandler()
    {
        var handler = new RecordingHandler(0, []);
        var adapter = CreateAdapter([handler]);
        var domainEvent = new SampleEvent("payload");

        await adapter.HandleAsync(domainEvent);

        handler.Received.Should().ContainSingle().Which.Should().Be(domainEvent);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleHandlers_InvokesInAscendingOrder()
    {
        var tracker = new List<int>();
        var adapter = CreateAdapter(
        [
            new RecordingHandler(10, tracker),
            new RecordingHandler(0, tracker),
            new RecordingHandler(5, tracker)
        ]);

        await adapter.HandleAsync(new SampleEvent("x"));

        tracker.Should().BeEquivalentTo([0, 5, 10], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task HandleAsync_WithNoHandlers_DoesNotThrow()
    {
        var adapter = CreateAdapter([]);

        await adapter.Invoking(a => a.HandleAsync(new SampleEvent("x"))).Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_PassesGeneratedContextWithMessageId_ToHandler()
    {
        var handler = new ContextCapturingHandler();
        var adapter = CreateAdapter([handler]);

        await adapter.HandleAsync(new SampleEvent("x"));

        handler.Captured.Should().NotBeNull();
        handler.Captured!.MessageId.Should().NotBeNullOrEmpty();
    }
}
