using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Audit;
using Pragmatic.Messaging.Auditing;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     End-to-end integration tests: DI container → publish → handler → audit.
///     Uses InMemory transport (no Docker required).
/// </summary>
public class MessagingIntegrationTests
{
    public record OrderPlaced(Guid OrderId, decimal Total);
    public record InvoiceCreated(Guid InvoiceId, Guid OrderId);

    public class OrderPlacedHandler : IMessageHandler<OrderPlaced>
    {
        public List<OrderPlaced> Received { get; } = [];

        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    public class InvoiceCreatedHandler : IMessageHandler<InvoiceCreated>
    {
        public List<InvoiceCreated> Received { get; } = [];

        public Task HandleAsync(InvoiceCreated message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_InMemory_ShouldDeliverToRegisteredHandler()
    {
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler);

        var bus = sp.GetRequiredService<IMessageBus>();
        var message = new OrderPlaced(Guid.NewGuid(), 99.99m);

        await bus.PublishAsync(message);

        handler.Received.Should().ContainSingle().Which.Total.Should().Be(99.99m);
    }

    [Fact]
    public async Task PublishAsync_WithAuditing_ShouldRecordAuditEntry()
    {
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler, enableAuditing: true);

        var bus = sp.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 50m));

        // Asserts the entry, not the registration. The previous version of this test only checked that
        // a store resolved, which passed whether or not anything was ever recorded.
        var trail = (CapturingAuditTrail)sp.GetRequiredService<IAuditTrail>();
        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Category.Should().Be(AuditCategory.Message);
        entry.Outcome.Should().Be(AuditOutcome.Success);
        entry.TargetType.Should().Contain(nameof(OrderPlaced));
    }

    /// <summary>Collects what the pipeline records, so the assertion can be about behaviour.</summary>
    private sealed class CapturingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return default;
        }
    }

    [Fact]
    public async Task PublishAsync_MultipleMessageTypes_ShouldRouteCorrectly()
    {
        var orderHandler = new OrderPlacedHandler();
        var invoiceHandler = new InvoiceCreatedHandler();
        await using var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IMessageHandler<OrderPlaced>>(orderHandler);
            services.AddSingleton<IMessageHandler<InvoiceCreated>>(invoiceHandler);
        });

        var bus = sp.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 100m));
        await bus.PublishAsync(new InvoiceCreated(Guid.NewGuid(), Guid.NewGuid()));

        orderHandler.Received.Should().ContainSingle();
        invoiceHandler.Received.Should().ContainSingle();
    }

    [Fact]
    public async Task PublishAsync_WithContext_ShouldPropagateCorrelation()
    {
        MessageContext? capturedCtx = null;
        var handler = new ContextCapture(ctx => capturedCtx = ctx);
        await using var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IMessageHandler<OrderPlaced>>(handler);
        });

        var bus = sp.GetRequiredService<IMessageBus>();
        var context = MessageContext.New(correlationId: "order-flow-123", tenantId: "acme");
        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 75m), context);

        capturedCtx.Should().NotBeNull();
        capturedCtx!.CorrelationId.Should().Be("order-flow-123");
        capturedCtx.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task PublishAsync_FailingHandler_RunsSiblingsThenThrows()
    {
        var goodHandler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IMessageHandler<OrderPlaced>>(new FailingHandler());
            services.AddSingleton<IMessageHandler<OrderPlaced>>(goodHandler);
        });

        var bus = sp.GetRequiredService<IMessageBus>();
        // W1 honest-outcome contract: siblings still run, but the failure propagates so the
        // transport consume path can nack/dead-letter instead of acking a failed delivery.
        await bus.Invoking(b => b.PublishAsync(new OrderPlaced(Guid.NewGuid(), 25m)))
            .Should().ThrowAsync<AggregateException>();

        goodHandler.Received.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_UntypedObject_ShouldResolveAndDeliver()
    {
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler);

        var bus = sp.GetRequiredService<IMessageBus>();
        object message = new OrderPlaced(Guid.NewGuid(), 42m);
        await bus.DispatchAsync(message, MessageContext.New());

        handler.Received.Should().ContainSingle().Which.Total.Should().Be(42m);
    }

    [Fact]
    public async Task Idempotency_SameMessageId_ShouldOnlyProcessOnce()
    {
        var store = new InMemoryIdempotencyStore();
        var first = await store.TryMarkAsProcessedAsync("msg-dedup-1");
        var second = await store.TryMarkAsProcessedAsync("msg-dedup-1");

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task TransportBus_Dispatch_WithIdempotencyStore_DropsRedeliveredMessageId()
    {
        // Redelivery on a transport reuses the same MessageId. The TransportAwareMessageBus
        // consume path (DispatchAsync) must dedup so handlers run at most once.
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler);

        var local = new InMemoryMessageBus(
            sp,
            sp.GetRequiredService<ILogger<InMemoryMessageBus>>(),
            sp.GetServices<ITypedMessageDispatchTable>());

        var bus = new TransportAwareMessageBus(
            new NoopTransport(),
            new DefaultMessageRouter(),
            new JsonMessageSerializer(),
            local,
            sp.GetRequiredService<ILogger<TransportAwareMessageBus>>(),
            new InMemoryIdempotencyStore());

        var context = MessageContext.New();
        object message = new OrderPlaced(Guid.NewGuid(), 10m);

        await bus.DispatchAsync(message, context);
        await bus.DispatchAsync(message, context); // redelivery, same MessageId

        handler.Received.Should().ContainSingle("the duplicate dispatch must be dropped by idempotency");
    }

    [Fact]
    public async Task TransportBus_Dispatch_WithoutIdempotencyStore_DeliversEveryTime()
    {
        // When no store is registered, dedup is opt-out: at-least-once is preserved.
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler);

        var local = new InMemoryMessageBus(
            sp,
            sp.GetRequiredService<ILogger<InMemoryMessageBus>>(),
            sp.GetServices<ITypedMessageDispatchTable>());

        var bus = new TransportAwareMessageBus(
            new NoopTransport(),
            new DefaultMessageRouter(),
            new JsonMessageSerializer(),
            local,
            sp.GetRequiredService<ILogger<TransportAwareMessageBus>>());

        var context = MessageContext.New();
        object message = new OrderPlaced(Guid.NewGuid(), 10m);

        await bus.DispatchAsync(message, context);
        await bus.DispatchAsync(message, context);

        handler.Received.Should().HaveCount(2);
    }

    private sealed class NoopTransport : IMessageTransport
    {
        public string Name => "noop";
        public TransportStatus Status => TransportStatus.Connected;
        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IAsyncDisposable> SubscribeAsync(string topic, string subscriptionName, Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new NoopSubscription());
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class NoopSubscription : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ServiceProvider BuildServiceProvider(OrderPlacedHandler handler, bool enableAuditing = false)
    {
        return BuildServiceProvider(services =>
        {
            services.AddSingleton<IMessageHandler<OrderPlaced>>(handler);
        }, enableAuditing);
    }

    private static ServiceProvider BuildServiceProvider(Action<IServiceCollection> configure, bool enableAuditing = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(msg =>
        {
            if (enableAuditing)
            {
                services.AddSingleton<IAuditTrail, CapturingAuditTrail>();
                msg.EnableAuditing();
            }
        });
        configure(services);
        return services.BuildServiceProvider();
    }

    private class FailingHandler : IMessageHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
            => throw new InvalidOperationException("Simulated failure");
    }

    private class ContextCapture(Action<MessageContext> capture) : IMessageHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            capture(context);
            return Task.CompletedTask;
        }
    }
}
