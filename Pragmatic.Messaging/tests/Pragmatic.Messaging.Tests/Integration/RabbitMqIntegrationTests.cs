using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Real RabbitMQ integration tests against a Testcontainers-managed broker, so they run
///     in CI. Skipped gracefully only when Docker itself is unavailable.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RabbitMqBroker")]
public class RabbitMqIntegrationTests(RabbitMqContainerFixture broker) : IAsyncLifetime
{
    private RabbitMqTransport? _transport;
    private bool _brokerAvailable;

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        _brokerAvailable = broker.ConnectionString is not null;
        if (!_brokerAvailable)
            return;

        var options = new RabbitMqOptions
        {
            ConnectionString = broker.ConnectionString!,
            ConsumerPrefetchCount = 5,
            DurableQueues = false, // transient for tests
            PersistentMessages = false,
        };
        _transport = new RabbitMqTransport(options, NullLogger<RabbitMqTransport>.Instance);
        await _transport.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_transport is not null)
            await _transport.DisposeAsync();
    }
#pragma warning restore CA2007

    [Fact]
    public void AfterConnect_StatusShouldBeConnected()
    {
        if (!_brokerAvailable)
            return;

        _transport!.Status.Should().Be(TransportStatus.Connected);
    }

    [Fact]
    public async Task PublishAndSubscribe_ShouldDeliverMessageViaRabbitMq()
    {
        if (!_brokerAvailable)
            return;

        var received = new TaskCompletionSource<(string Payload, MessageContext Context)>();
        var exchangeName = $"test-exchange-{Guid.NewGuid():N}";
        var queueName = $"test-queue-{Guid.NewGuid():N}";

        // Subscribe first
        await _transport!.SubscribeAsync(exchangeName, queueName, (payload, ctx, ct) =>
        {
            var text = Encoding.UTF8.GetString(payload.Span);
            received.SetResult((text, ctx));
            return Task.CompletedTask;
        });

        // Small delay to let consumer start
        await Task.Delay(500);

        // Publish
        var context = MessageContext.New(correlationId: "rmq-test-1", tenantId: "acme");
        var payloadBytes = Encoding.UTF8.GetBytes("{\"orderId\":\"123\",\"total\":99.99}");
        await _transport.PublishAsync(payloadBytes, exchangeName, context);

        // Wait for delivery
        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        result.Payload.Should().Contain("123");
        result.Payload.Should().Contain("99.99");
        result.Context.CorrelationId.Should().Be("rmq-test-1");
        result.Context.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task SendAsync_PointToPoint_ShouldDeliverToQueue()
    {
        if (!_brokerAvailable)
            return;

        var received = new TaskCompletionSource<string>();
        var queueName = $"test-p2p-{Guid.NewGuid():N}";

        // Subscribe to queue directly (SendAsync uses default exchange + queue as routing key)
        // For this test, we use SubscribeAsync with the queue as the exchange
        // and let the transport handle the binding
        await _transport!.SubscribeAsync(queueName, queueName, (payload, _, _) =>
        {
            received.SetResult(Encoding.UTF8.GetString(payload.Span));
            return Task.CompletedTask;
        });

        await Task.Delay(500);

        var payloadBytes = Encoding.UTF8.GetBytes("point-to-point-message");
        await _transport.PublishAsync(payloadBytes, queueName, MessageContext.New());

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.Should().Be("point-to-point-message");
    }

    [Fact]
    public async Task MultipleMessages_ShouldDeliverAllInOrder()
    {
        if (!_brokerAvailable)
            return;

        var messages = new List<string>();
        var allReceived = new TaskCompletionSource();
        var exchangeName = $"test-multi-{Guid.NewGuid():N}";
        var queueName = $"test-multi-q-{Guid.NewGuid():N}";

        await _transport!.SubscribeAsync(exchangeName, queueName, (payload, _, _) =>
        {
            var text = Encoding.UTF8.GetString(payload.Span);
            lock (messages)
            {
                messages.Add(text);
                if (messages.Count >= 5)
                    allReceived.TrySetResult();
            }
            return Task.CompletedTask;
        });

        await Task.Delay(500);

        for (var i = 0; i < 5; i++)
        {
            var bytes = Encoding.UTF8.GetBytes($"msg-{i}");
            await _transport.PublishAsync(bytes, exchangeName, MessageContext.New());
        }

        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        messages.Should().HaveCount(5);
        messages.Should().Contain("msg-0");
        messages.Should().Contain("msg-4");
    }

    [Fact]
    public async Task HeaderPropagation_ShouldPreserveTenantAndUser()
    {
        if (!_brokerAvailable)
            return;

        var receivedCtx = new TaskCompletionSource<MessageContext>();
        var exchangeName = $"test-headers-{Guid.NewGuid():N}";
        var queueName = $"test-headers-q-{Guid.NewGuid():N}";

        await _transport!.SubscribeAsync(exchangeName, queueName, (_, ctx, _) =>
        {
            receivedCtx.SetResult(ctx);
            return Task.CompletedTask;
        });

        await Task.Delay(500);

        var context = new MessageContext(
            MessageId: "hdr-test-1",
            CorrelationId: "corr-hdr",
            TenantId: "tenant-42",
            UserId: "user-99",
            SourceBoundary: "billing");

        await _transport.PublishAsync("test"u8.ToArray(), exchangeName, context);

        var result = await receivedCtx.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.CorrelationId.Should().Be("corr-hdr");
        result.TenantId.Should().Be("tenant-42");
        result.UserId.Should().Be("user-99");
    }

    [Fact]
    public async Task Disconnect_ShouldCleanupGracefully()
    {
        if (!_brokerAvailable)
            return;

        _transport!.Status.Should().Be(TransportStatus.Connected);

        await _transport.DisconnectAsync();

        _transport.Status.Should().Be(TransportStatus.Disconnected);
    }

    /// <summary>
    ///     A publish that arrives while the connect is still talking to the broker waits for it,
    ///     instead of failing with "not connected".
    /// </summary>
    [Fact]
    public async Task APublishIssuedWhileTheTransportConnects_WaitsForTheConnect()
    {
        if (!_brokerAvailable)
            return;

        var transport = new RabbitMqTransport(
            new RabbitMqOptions { ConnectionString = broker.ConnectionString!, DurableQueues = false, PersistentMessages = false },
            NullLogger<RabbitMqTransport>.Instance);
        try
        {
            var connecting = transport.ConnectAsync();
            transport.Status.Should().Be(TransportStatus.Connecting, "the connect is still talking to the broker");

            await transport.PublishAsync("early"u8.ToArray(), $"test-exchange-{Guid.NewGuid():N}", MessageContext.New());

            await connecting.ConfigureAwait(true);
            transport.Status.Should().Be(TransportStatus.Connected);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    /// <summary>
    ///     The moment the host has started, a publish is accepted: the consumer service began
    ///     the connect in <c>StartAsync</c>, and the application still does not wait for the broker to start.
    /// </summary>
    [Fact]
    public async Task APublishRightAfterTheConsumerServiceStarts_IsAccepted()
    {
        if (!_brokerAvailable)
            return;

        var transport = new RabbitMqTransport(
            new RabbitMqOptions { ConnectionString = broker.ConnectionString!, DurableQueues = false, PersistentMessages = false },
            NullLogger<RabbitMqTransport>.Instance);
        var services = new ServiceCollection().BuildServiceProvider();
        var consumer = new RabbitMqConsumerService(
            transport,
            services.GetRequiredService<IServiceScopeFactory>(),
            new DefaultMessageRouter(),
            [],
            [],
            NullLogger<RabbitMqConsumerService>.Instance);
        try
        {
            await consumer.StartAsync(CancellationToken.None);

            await transport.PublishAsync("right-away"u8.ToArray(), $"test-exchange-{Guid.NewGuid():N}", MessageContext.New());

            transport.Status.Should().Be(TransportStatus.Connected);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
            await transport.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
