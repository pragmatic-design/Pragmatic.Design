using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.Routing;
using ChannelOptions = Pragmatic.Messaging.Channels.ChannelOptions;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Verifies the transport subscription wiring (C4/K1): a message published on a transport is
///     deserialized and dispatched to local handlers. Without <see cref="TransportSubscriptionBinder"/>
///     the consumer services never subscribe and published messages are silently lost.
/// </summary>
#pragma warning disable CA2007 // xUnit manages SynchronizationContext
public class TransportSubscriptionBinderTests
{
    public sealed record TestEvent(string Name);

    [Fact]
    public async Task BindAsync_PublishedMessage_IsDeserializedAndDispatched()
    {
        var options = new ChannelOptions { Capacity = 100, ConsumerCount = 1 };
        await using var transport = new ChannelTransport(options, NullLogger<ChannelTransport>.Instance);
        await transport.ConnectAsync();

        var router = new DefaultMessageRouter();
        var serializer = new JsonMessageSerializer();
        var recordingBus = new RecordingMessageBus();

        var services = new ServiceCollection();
        services.AddSingleton<IMessageBus>(recordingBus);
        services.AddSingleton<IMessageSerializer>(serializer);
        await using var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var subscriptions = new[] { new MessageSubscription(typeof(TestEvent), subscriber: "binder") };
        var handles = await TransportSubscriptionBinder.BindAsync(transport, router, scopeFactory, subscriptions);
        handles.Should().HaveCount(1);

        var topic = router.GetTopic(typeof(TestEvent));
        var bytes = serializer.Serialize(new TestEvent("hello"));
        await transport.PublishAsync(bytes, topic, MessageContext.New());

        await recordingBus.Dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        recordingBus.LastMessage.Should().BeOfType<TestEvent>();
        ((TestEvent)recordingBus.LastMessage!).Name.Should().Be("hello");

        foreach (var handle in handles)
            await handle.DisposeAsync();
    }

    /// <summary>Records the message dispatched via <see cref="IMessageBus.DispatchAsync"/>.</summary>
    private sealed class RecordingMessageBus : IMessageBus
    {
        public object? LastMessage { get; private set; }
        public TaskCompletionSource Dispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
        {
            LastMessage = message;
            Dispatched.TrySetResult();
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }
}
