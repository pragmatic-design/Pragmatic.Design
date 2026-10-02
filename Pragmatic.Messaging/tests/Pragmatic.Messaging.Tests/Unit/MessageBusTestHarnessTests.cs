using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Testing;

namespace Pragmatic.Messaging.Tests.Unit;

public class MessageBusTestHarnessTests
{
    public record OrderCreated(Guid OrderId, decimal Total);
    public record OrderShipped(Guid OrderId, string TrackingCode);

    [Fact]
    public async Task PublishAsync_RecordsMessage()
    {
        var harness = new MessageBusTestHarness();
        var msg = new OrderCreated(Guid.NewGuid(), 42.50m);

        await harness.PublishAsync(msg);

        harness.Published.Should().HaveCount(1);
        harness.Published[0].MessageType.Should().Be(typeof(OrderCreated));
        harness.Published[0].Message.Should().Be(msg);
        harness.Published[0].Context.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishAsync_WithContext_RecordsContext()
    {
        var harness = new MessageBusTestHarness();
        var ctx = new MessageContext(MessageId: "test-123", CorrelationId: "corr-1");

        await harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 10m), ctx);

        harness.Published[0].Context!.MessageId.Should().Be("test-123");
        harness.Published[0].Context!.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task PublishedOf_FiltersCorrectType()
    {
        var harness = new MessageBusTestHarness();
        var order = new OrderCreated(Guid.NewGuid(), 100m);
        var ship = new OrderShipped(Guid.NewGuid(), "TR-001");

        await harness.PublishAsync(order);
        await harness.PublishAsync(ship);

        harness.PublishedOf<OrderCreated>().Should().ContainSingle().Which.Should().Be(order);
        harness.PublishedOf<OrderShipped>().Should().ContainSingle().Which.TrackingCode.Should().Be("TR-001");
    }

    [Fact]
    public async Task HasPublished_ReturnsTrueWhenMatching()
    {
        var harness = new MessageBusTestHarness();
        await harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 99m));

        harness.HasPublished<OrderCreated>().Should().BeTrue();
        harness.HasPublished<OrderShipped>().Should().BeFalse();
    }

    [Fact]
    public async Task HasPublished_WithPredicate_FiltersCorrectly()
    {
        var harness = new MessageBusTestHarness();
        await harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 50m));
        await harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 200m));

        harness.HasPublished<OrderCreated>(o => o.Total > 100m).Should().BeTrue();
        harness.HasPublished<OrderCreated>(o => o.Total > 500m).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_ClearsAllMessages()
    {
        var harness = new MessageBusTestHarness();
        await harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 10m));
        await harness.PublishAsync(new OrderShipped(Guid.NewGuid(), "TR-002"));

        harness.Reset();

        harness.Published.Should().BeEmpty();
        harness.HasPublished<OrderCreated>().Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_RecordsMessageAsSent()
    {
        var harness = new MessageBusTestHarness();
        var msg = new OrderCreated(Guid.NewGuid(), 75m);

        await harness.SendAsync(msg);

        // Point-to-point sends are tracked separately from fan-out publishes.
        harness.HasSent<OrderCreated>().Should().BeTrue();
        harness.SentOf<OrderCreated>().Should().ContainSingle().Which.Should().Be(msg);
        harness.HasPublished<OrderCreated>().Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_RecordsUntypedMessage()
    {
        var harness = new MessageBusTestHarness();
        object msg = new OrderShipped(Guid.NewGuid(), "TR-003");

        await harness.DispatchAsync(msg, MessageContext.New());

        harness.Published.Should().HaveCount(1);
        harness.Published[0].MessageType.Should().Be(typeof(OrderShipped));
    }

    [Fact]
    public async Task RequestAsync_ThrowsWithRecordedMessage()
    {
        var harness = new MessageBusTestHarness();

        var act = () => harness.RequestAsync<OrderCreated, string>(new OrderCreated(Guid.NewGuid(), 10m));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not support RequestAsync*");

        // Request is still recorded (as point-to-point)
        harness.HasSent<OrderCreated>().Should().BeTrue();
    }

    [Fact]
    public void AddMessagingTestHarness_RegistersHarness()
    {
        var services = new ServiceCollection();
        var harness = services.AddMessagingTestHarness();
        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IMessageBus>().Should().BeSameAs(harness);
        sp.GetRequiredService<MessageBusTestHarness>().Should().BeSameAs(harness);
    }

    [Fact]
    public void AddMessagingTestHarness_ReplacesExistingBus()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageBus>(new MessageBusTestHarness());
        var harness = services.AddMessagingTestHarness();
        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IMessageBus>().Should().BeSameAs(harness);
    }

    [Fact]
    public async Task ConcurrentPublish_IsThreadSafe()
    {
        var harness = new MessageBusTestHarness();
        var tasks = Enumerable.Range(0, 100)
            .Select(i => harness.PublishAsync(new OrderCreated(Guid.NewGuid(), i)));

        await Task.WhenAll(tasks);

        harness.Published.Should().HaveCount(100);
        harness.PublishedOf<OrderCreated>().Should().HaveCount(100);
    }

    // ── Dispatching mode (AddDispatchingTestHarness) ──

    [Fact]
    public async Task DispatchingHarness_DeliversToHandlers_AndTracksConsumed()
    {
        var handler = new RecordingOrderHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IMessageHandler<OrderCreated>>(handler);
        services.AddDispatchingTestHarness();
        await using var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<MessageBusTestHarness>();

        var msg = new OrderCreated(Guid.NewGuid(), 12m);
        await harness.PublishAsync(msg);

        handler.Received.Should().ContainSingle().Which.Should().Be(msg);
        harness.HasConsumed<OrderCreated>(m => m.Total == 12m).Should().BeTrue();
        harness.Consumed.Should().ContainSingle()
            .Which.HandlerType.Should().Be(typeof(RecordingOrderHandler));
        harness.Faulted.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchingHarness_HandlerFailure_IsRecordedNotThrown()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageHandler<OrderCreated>>(new ThrowingOrderHandler());
        services.AddSingleton<IMessageHandler<OrderCreated>>(new RecordingOrderHandler());
        services.AddDispatchingTestHarness();
        await using var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<MessageBusTestHarness>();

        // Test-harness contract: failures are recorded, not rethrown.
        await harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 1m));

        harness.HasFaulted<OrderCreated>().Should().BeTrue();
        harness.FaultedOf<OrderCreated>().Should().ContainSingle()
            .Which.Exception.Message.Should().Be("payment gateway down");
        // The sibling handler still ran (execution isolation).
        harness.HasConsumed<OrderCreated>().Should().BeTrue();
    }

    [Fact]
    public async Task DispatchingHarness_RequestAsync_ResolvesHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<OrderCreated, string>>(new OrderTotalRequestHandler());
        services.AddDispatchingTestHarness();
        await using var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<MessageBusTestHarness>();

        var response = await harness.RequestAsync<OrderCreated, string>(new OrderCreated(Guid.NewGuid(), 33m));

        response.Should().Be("33");
        harness.HasSent<OrderCreated>().Should().BeTrue();
        harness.HasConsumed<OrderCreated>().Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_RecordsInDispatchedBag_DistinctFromPublished()
    {
        var harness = new MessageBusTestHarness(); // record-only
        await harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 5m));
        await harness.DispatchAsync(new OrderShipped(Guid.NewGuid(), "TR-D"), MessageContext.New());

        harness.HasDispatched<OrderShipped>().Should().BeTrue();
        harness.DispatchedOf<OrderShipped>().Should().ContainSingle();
        // A producer Publish is not a local Dispatch.
        harness.HasDispatched<OrderCreated>().Should().BeFalse();

        harness.Reset();
        harness.Dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchingHarness_StrictMode_RethrowsHandlerFailure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageHandler<OrderCreated>>(new ThrowingOrderHandler());
        services.AddDispatchingTestHarness();
        await using var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<MessageBusTestHarness>();
        harness.RethrowHandlerFailures = true;

        var act = () => harness.PublishAsync(new OrderCreated(Guid.NewGuid(), 1m));

        // Strict mode surfaces the terminal failure — and still records it.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*payment gateway down*");
        harness.HasFaulted<OrderCreated>().Should().BeTrue();
    }

    [Fact]
    public async Task DispatchingHarness_UntypedWithoutDispatchTable_ThrowsLoudly()
    {
        var services = new ServiceCollection();
        services.AddDispatchingTestHarness();
        await using var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<MessageBusTestHarness>();

        // No ITypedMessageDispatchTable registered: an untyped dispatch would never reach handlers,
        // so the harness fails loudly instead of recording a false-negative success.
        Func<Task> act = () => harness.DispatchAsync(new OrderCreated(Guid.NewGuid(), 1m), MessageContext.New());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ITypedMessageDispatchTable*");
    }

    private sealed class RecordingOrderHandler : IMessageHandler<OrderCreated>
    {
        public List<OrderCreated> Received { get; } = [];

        public Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOrderHandler : IMessageHandler<OrderCreated>
    {
        public Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct)
            => throw new InvalidOperationException("payment gateway down");
    }

    private sealed class OrderTotalRequestHandler : IRequestHandler<OrderCreated, string>
    {
        public Task<string> HandleAsync(OrderCreated request, MessageContext context, CancellationToken ct)
            => Task.FromResult(request.Total.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
