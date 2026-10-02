using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Tests.Unit;

public class PartitionKeyTests
{
    [Fact]
    public async Task PublishAsync_WithResolver_StampsPartitionKeyHeader()
    {
        var transport = new CapturingPublishTransport();
        var bus = CreateBus(transport, new OrderRegionResolver());

        await bus.PublishAsync(new PartitionedOrder(Guid.NewGuid(), "eu-west"), MessageContext.New());

        transport.LastContext.Should().NotBeNull();
        transport.LastContext!.Headers.Should().ContainKey(IPartitionKeyResolver.HeaderName)
            .WhoseValue.Should().Be("eu-west");
    }

    [Fact]
    public async Task PublishAsync_ResolverUnknownType_LeavesHeadersUntouched()
    {
        var transport = new CapturingPublishTransport();
        var bus = CreateBus(transport, new OrderRegionResolver());

        await bus.PublishAsync(new UnkeyedMessage(7), MessageContext.New());

        (transport.LastContext!.Headers?.ContainsKey(IPartitionKeyResolver.HeaderName) ?? false)
            .Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_UntypedOverload_StampsPartitionKeyHeader()
    {
        var transport = new CapturingPublishTransport();
        var bus = CreateBus(transport, new OrderRegionResolver());

        object message = new PartitionedOrder(Guid.NewGuid(), "us-east");
        await bus.PublishAsync(message, typeof(PartitionedOrder), MessageContext.New());

        transport.LastContext!.Headers.Should().ContainKey(IPartitionKeyResolver.HeaderName)
            .WhoseValue.Should().Be("us-east");
    }

    private static TransportAwareMessageBus CreateBus(IMessageTransport transport, IPartitionKeyResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddPragmaticMessaging();
        var sp = services.BuildServiceProvider();

        return new TransportAwareMessageBus(
            transport,
            new DefaultMessageRouter(),
            new JsonMessageSerializer(),
            new InMemoryMessageBus(sp, NullLogger<InMemoryMessageBus>.Instance, sp.GetServices<ITypedMessageDispatchTable>()),
            NullLogger<TransportAwareMessageBus>.Instance,
            idempotencyStore: null,
            partitionKeyResolvers: [resolver]);
    }

    private sealed class OrderRegionResolver : IPartitionKeyResolver
    {
        public string? TryGetPartitionKey(object message)
            => message is PartitionedOrder order ? order.Region : null;
    }

    private sealed class CapturingPublishTransport : IMessageTransport
    {
        public MessageContext? LastContext { get; private set; }

        public string Name => "Capturing";
        public TransportStatus Status => TransportStatus.Connected;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
        {
            LastContext = context;
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
        {
            LastContext = context;
            return Task.CompletedTask;
        }

        public Task<IAsyncDisposable> SubscribeAsync(
            string topic,
            string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
            CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new Noop());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

public sealed record PartitionedOrder(Guid OrderId, string Region);

public sealed record UnkeyedMessage(int Id);
