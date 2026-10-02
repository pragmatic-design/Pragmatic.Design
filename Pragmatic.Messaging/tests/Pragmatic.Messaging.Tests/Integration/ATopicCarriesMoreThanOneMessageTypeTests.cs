using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     A subscriber receives the messages of its own type, and not everything the topic
///     carries.
/// </summary>
/// <remarks>
///     <para>
///         <b>A topic is a boundary, not a type.</b> <c>DefaultMessageRouter.GetTopic</c> answers
///         <c>{boundary}.events</c> for every message of a module, so two message types published by one
///         boundary share it. Each subscription is its own queue bound to that topic, so the broker
///         delivers <b>both</b> messages to <b>both</b> queues — and the consumer deserializes whatever
///         arrives into the type its subscription was created for.
///     </para>
///     <para>
///         ⚠️ What that produces is not an error: it is a message of the right shape and the wrong
///         content. JSON deserialization fills what matches and leaves the rest at its default, so a
///         <c>VerificationAnswered</c> arriving at a <c>TenantOnboarded</c> handler becomes a
///         <c>TenantOnboarded</c> with an empty tenant id and a plausible timestamp.
///     </para>
///     <para>
///         A service that handles <b>one</b> message type per publishing boundary cannot show it. The
///         second one is what makes the topic ambiguous.
///     </para>
/// </remarks>
#pragma warning disable CA2007 // xUnit manages SynchronizationContext
public class ATopicCarriesMoreThanOneMessageTypeTests
{
    /// <summary>Two types from one namespace, so one topic — which is the whole point.</summary>
    public sealed record SomethingWasAsked(string What);

    public sealed record SomebodyWasOnboarded(string Who);

    [Fact]
    public async Task ASubscriberReceivesOnlyItsOwnType()
    {
        await using var transport = new AnExchangeThatFansOut();
        await transport.ConnectAsync();

        var router = new DefaultMessageRouter();
        var serializer = new JsonMessageSerializer();
        var bus = new CollectingBus();

        var services = new ServiceCollection();
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton<IMessageSerializer>(serializer);
        await using var provider = services.BuildServiceProvider();

        var handles = await TransportSubscriptionBinder.BindAsync(
            transport,
            router,
            provider.GetRequiredService<IServiceScopeFactory>(),
            [new MessageSubscription(typeof(SomethingWasAsked), subscriber: "verify"),
                new MessageSubscription(typeof(SomebodyWasOnboarded), subscriber: "verify")]);

        // Both subscriptions resolve to the same topic — that is the situation, not a mistake in the test.
        router.GetTopic(typeof(SomethingWasAsked)).Should()
            .Be(router.GetTopic(typeof(SomebodyWasOnboarded)));

        var message = new SomebodyWasOnboarded("wayland");
        await transport.PublishAsync(
            serializer.Serialize(message),
            router.GetTopic(typeof(SomebodyWasOnboarded)),
            MessageContext.New() with { MessageType = typeof(SomebodyWasOnboarded).FullName });

        // Give the other subscription every chance to receive it too.
        await Task.Delay(TimeSpan.FromSeconds(1));

        bus.Dispatched.Should().ContainSingle(
            "one message was published, so one handler chain should have run")
            .Which.Should().BeOfType<SomebodyWasOnboarded>();

        foreach (var handle in handles)
            await handle.DisposeAsync();
    }

    /// <summary>
    ///     A transport shaped like the broker: one exchange per topic, every queue bound to it with no
    ///     routing key, so a publish reaches <b>every</b> subscription of that topic.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Written out rather than using <c>ChannelTransport</c>, which delivers a topic's message to
    ///     one subscription and would make this test pass without the type check. What is being reproduced is
    ///     <c>RabbitMqTransport</c>: <c>BasicPublishAsync(exchange: topic, routingKey: "")</c> and
    ///     <c>QueueBindAsync(subscriptionName, topic, "")</c>, which is a fan-out over the boundary.
    /// </remarks>
    private sealed class AnExchangeThatFansOut : IMessageTransport
    {
        private readonly List<(string Topic, Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> Handler)>
            _bindings = [];

        public string Name => "fan-out";

        public TransportStatus Status { get; private set; } = TransportStatus.Disconnected;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            Status = TransportStatus.Connected;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            Status = TransportStatus.Disconnected;
            return Task.CompletedTask;
        }

        public async Task PublishAsync(
            ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
        {
            foreach (var (boundTopic, handler) in _bindings.Where(b => b.Topic == topic).ToArray())
                await handler(payload, context, ct);
        }

        public Task SendAsync(
            ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IAsyncDisposable> SubscribeAsync(
            string topic,
            string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
            CancellationToken ct = default)
        {
            _bindings.Add((topic, handler));
            return Task.FromResult<IAsyncDisposable>(new NothingToUnbind());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class NothingToUnbind : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CollectingBus : IMessageBus
    {
        private readonly List<object> _dispatched = [];

        public IReadOnlyList<object> Dispatched
        {
            get
            {
                lock (_dispatched)
                    return [.. _dispatched];
            }
        }

        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
        {
            lock (_dispatched)
                _dispatched.Add(message);

            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default)
            where T : notnull => throw new NotSupportedException();

        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default)
            where T : notnull => throw new NotSupportedException();

        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }
}
