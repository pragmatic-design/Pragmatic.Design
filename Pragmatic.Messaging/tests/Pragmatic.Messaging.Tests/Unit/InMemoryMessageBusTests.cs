using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.Messaging.Tests.Unit;

public class InMemoryMessageBusTests
{
    public record OrderPlaced(Guid OrderId, decimal Total);
    public record OrderCancelled(Guid OrderId);

    public class OrderPlacedHandler : IMessageHandler<OrderPlaced>
    {
        public List<OrderPlaced> Received { get; } = [];
        public int Order => 0;

        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    public class SecondOrderHandler : IMessageHandler<OrderPlaced>
    {
        public List<OrderPlaced> Received { get; } = [];
        public int Order => 10;

        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    public class FailingHandler : IMessageHandler<OrderPlaced>
    {
        public int CallCount { get; private set; }
        public int Order => 5;

        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            CallCount++;
            throw new InvalidOperationException("Handler failed");
        }
    }

    /// <summary>
    ///     Test dispatch table that handles the message types used in this test class.
    /// </summary>
    private sealed class TestMessageDispatchTable : ITypedMessageDispatchTable
    {
        public Task? TryDispatch(IMessageBus bus, object message, MessageContext context, CancellationToken ct)
            => message switch
            {
                OrderPlaced m => bus.PublishAsync(m, context, ct),
                OrderCancelled m => bus.PublishAsync(m, context, ct),
                _ => null
            };
    }

    private static readonly ITypedMessageDispatchTable DispatchTable = new TestMessageDispatchTable();

    private static (InMemoryMessageBus bus, ServiceProvider sp) CreateBus(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        configure(services);
        var sp = services.BuildServiceProvider();
        var bus = new InMemoryMessageBus(sp, sp.GetRequiredService<ILogger<InMemoryMessageBus>>(), [DispatchTable]);
        return (bus, sp);
    }

    [Fact]
    public async Task PublishAsync_WithHandler_ShouldDeliverMessage()
    {
        var handler = new OrderPlacedHandler();
        var (bus, sp) = CreateBus(s => s.AddSingleton<IMessageHandler<OrderPlaced>>(handler));

        var message = new OrderPlaced(Guid.NewGuid(), 99.99m);
        await bus.PublishAsync(message);

        handler.Received.Should().ContainSingle().Which.Should().Be(message);
        sp.Dispose();
    }

    [Fact]
    public async Task PublishAsync_WithMultipleHandlers_ShouldDeliverToAll()
    {
        var handler1 = new OrderPlacedHandler();
        var handler2 = new SecondOrderHandler();
        var (bus, sp) = CreateBus(s =>
        {
            s.AddSingleton<IMessageHandler<OrderPlaced>>(handler1);
            s.AddSingleton<IMessageHandler<OrderPlaced>>(handler2);
        });

        var message = new OrderPlaced(Guid.NewGuid(), 50m);
        await bus.PublishAsync(message);

        handler1.Received.Should().ContainSingle();
        handler2.Received.Should().ContainSingle();
        sp.Dispose();
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_ShouldNotThrow()
    {
        var (bus, sp) = CreateBus(_ => { });

        var message = new OrderPlaced(Guid.NewGuid(), 10m);
        await bus.Invoking(b => b.PublishAsync(message)).Should().NotThrowAsync();
        sp.Dispose();
    }

    [Fact]
    public async Task PublishAsync_FailingHandler_ShouldNotStopOtherHandlers()
    {
        // Execution is isolated (every handler runs) but the outcome is honest (throws).
        var failingHandler = new FailingHandler();
        var successHandler = new SecondOrderHandler();
        var (bus, sp) = CreateBus(s =>
        {
            s.AddSingleton<IMessageHandler<OrderPlaced>>(failingHandler);
            s.AddSingleton<IMessageHandler<OrderPlaced>>(successHandler);
        });

        var message = new OrderPlaced(Guid.NewGuid(), 25m);
        await bus.Invoking(b => b.PublishAsync(message)).Should().ThrowAsync<AggregateException>();

        failingHandler.CallCount.Should().Be(1);
        successHandler.Received.Should().ContainSingle("a failing sibling must not stop the fan-out");
        sp.Dispose();
    }

    [Fact]
    public async Task PublishAsync_AllHandlersFail_ThrowsAggregate_DocumentsHonestOutcomeContract()
    {
        // W1 contract (deliberate revision of the old K6 fault-isolation contract): execution stays
        // isolated — every handler runs regardless of siblings failing — but the OUTCOME is honest:
        // PublishAsync throws AggregateException when any handler failed. Without this, the transport
        // consume path acked/committed failed deliveries (RabbitMQ nack→DLX and Kafka DLQ were dead
        // code for handler failures) and the in-memory outbox marked failed deliveries as delivered.
        var failing = new FailingHandler();
        var (bus, sp) = CreateBus(s => s.AddSingleton<IMessageHandler<OrderPlaced>>(failing));

        var act = () => bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
        (await act.Should().ThrowAsync<AggregateException>())
            .Which.InnerExceptions.Should().ContainSingle();

        failing.CallCount.Should().Be(1);
        sp.Dispose();
    }

    [Fact]
    public async Task PublishAsync_WithContext_ShouldPassContextToHandler()
    {
        MessageContext? receivedContext = null;
        var handler = new ContextCapturingHandler(ctx => receivedContext = ctx);
        var (bus, sp) = CreateBus(s => s.AddSingleton<IMessageHandler<OrderPlaced>>(handler));

        var context = MessageContext.New(correlationId: "test-corr", tenantId: "t1");
        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 10m), context);

        receivedContext.Should().NotBeNull();
        receivedContext!.CorrelationId.Should().Be("test-corr");
        receivedContext.TenantId.Should().Be("t1");
        sp.Dispose();
    }

    [Fact]
    public async Task DispatchAsync_UntypedObject_ShouldResolveHandler()
    {
        var handler = new OrderPlacedHandler();
        var (bus, sp) = CreateBus(s => s.AddSingleton<IMessageHandler<OrderPlaced>>(handler));

        object message = new OrderPlaced(Guid.NewGuid(), 77m);
        await bus.DispatchAsync(message, MessageContext.New());

        handler.Received.Should().ContainSingle();
        sp.Dispose();
    }

    [Fact]
    public async Task PublishAsync_HandlersExecuteInOrder()
    {
        var order = new List<int>();
        var handler1 = new OrderTrackingHandler(0, order);
        var handler2 = new OrderTrackingHandler(10, order);
        var handler3 = new OrderTrackingHandler(5, order);
        var (bus, sp) = CreateBus(s =>
        {
            s.AddSingleton<IMessageHandler<OrderPlaced>>(handler1);
            s.AddSingleton<IMessageHandler<OrderPlaced>>(handler2);
            s.AddSingleton<IMessageHandler<OrderPlaced>>(handler3);
        });

        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 10m));

        order.Should().BeEquivalentTo([0, 5, 10], options => options.WithStrictOrdering());
        sp.Dispose();
    }

    private class ContextCapturingHandler(Action<MessageContext> capture) : IMessageHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            capture(context);
            return Task.CompletedTask;
        }
    }

    private class OrderTrackingHandler(int order, List<int> tracker) : IMessageHandler<OrderPlaced>
    {
        public int Order => order;

        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            tracker.Add(order);
            return Task.CompletedTask;
        }
    }
}
