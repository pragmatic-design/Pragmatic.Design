using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Kafka;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Real Kafka integration tests against a Testcontainers-managed broker, so they run
///     in CI. Skipped gracefully only when Docker itself is unavailable.
/// </summary>
[Trait("Category", "Integration")]
[Collection("KafkaBroker")]
public class KafkaIntegrationTests(KafkaContainerFixture broker) : IAsyncLifetime
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    private KafkaTransport? _transport;
    private bool _brokerAvailable;

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        _brokerAvailable = broker.BootstrapServers is not null;
        if (!_brokerAvailable)
            return;

        var options = new KafkaOptions
        {
            BootstrapServers = broker.BootstrapServers!,
            EnableIdempotence = true,
            EnableAutoCommit = false,
            AutoOffsetReset = "earliest",
        };
        _transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);
        await _transport.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_transport is not null)
            await _transport.DisposeAsync();
    }
#pragma warning restore CA2007

    [Fact]
    public async Task PublishAndConsume_RoundTrip()
    {
        if (!_brokerAvailable)
        {
            // No Kafka broker — skip gracefully
            return;
        }

        var received = new TaskCompletionSource<(string Payload, MessageContext Context)>();
        var topic = $"test-roundtrip-{Guid.NewGuid():N}";
        var groupId = $"test-group-{Guid.NewGuid():N}";

        await _transport!.SubscribeAsync(topic, groupId, (payload, ctx, ct) =>
        {
            var text = Encoding.UTF8.GetString(payload.Span);
            received.TrySetResult((text, ctx));
            return Task.CompletedTask;
        });

        // Allow consumer to join the group and start polling
        await Task.Delay(2000);

        var context = MessageContext.New(correlationId: "kafka-test-1", tenantId: "acme");
        var payloadBytes = Encoding.UTF8.GetBytes("{\"orderId\":\"456\",\"total\":199.99}");
        await _transport.PublishAsync(payloadBytes, topic, context);

        var result = await received.Task.WaitAsync(ReceiveTimeout);

        result.Payload.Should().Contain("456");
        result.Payload.Should().Contain("199.99");
        result.Context.CorrelationId.Should().Be("kafka-test-1");
        result.Context.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task MultipleMessages_DeliveredInOrder()
    {
        if (!_brokerAvailable)
            return;

        var messages = new List<string>();
        var allReceived = new TaskCompletionSource();
        var topic = $"test-order-{Guid.NewGuid():N}";
        var groupId = $"test-order-group-{Guid.NewGuid():N}";

        await _transport!.SubscribeAsync(topic, groupId, (payload, _, _) =>
        {
            var text = Encoding.UTF8.GetString(payload.Span);
            lock (messages)
            {
                messages.Add(text);
                if (messages.Count >= 3)
                    allReceived.TrySetResult();
            }
            return Task.CompletedTask;
        });

        await Task.Delay(2000);

        // Use the same correlation ID as message key so all messages go to the same
        // partition — this guarantees ordering within Kafka.
        for (var i = 0; i < 3; i++)
        {
            var bytes = Encoding.UTF8.GetBytes($"msg-{i}");
            var ctx = MessageContext.New(correlationId: "same-key");
            await _transport.PublishAsync(bytes, topic, ctx);
        }

        await allReceived.Task.WaitAsync(ReceiveTimeout);

        messages.Should().HaveCount(3);
        messages[0].Should().Be("msg-0");
        messages[1].Should().Be("msg-1");
        messages[2].Should().Be("msg-2");
    }

    [Fact]
    public async Task Headers_PreservedAcrossTransport()
    {
        if (!_brokerAvailable)
            return;

        var receivedCtx = new TaskCompletionSource<MessageContext>();
        var topic = $"test-headers-{Guid.NewGuid():N}";
        var groupId = $"test-headers-group-{Guid.NewGuid():N}";

        await _transport!.SubscribeAsync(topic, groupId, (_, ctx, _) =>
        {
            receivedCtx.TrySetResult(ctx);
            return Task.CompletedTask;
        });

        await Task.Delay(2000);

        var context = new MessageContext(
            MessageId: "hdr-kafka-1",
            CorrelationId: "corr-kafka",
            TenantId: "tenant-42",
            UserId: "user-99");

        await _transport.PublishAsync("test"u8.ToArray(), topic, context);

        var result = await receivedCtx.Task.WaitAsync(ReceiveTimeout);

        result.MessageId.Should().Be("hdr-kafka-1");
        result.CorrelationId.Should().Be("corr-kafka");
        result.TenantId.Should().Be("tenant-42");
        result.UserId.Should().Be("user-99");
    }

    [Fact]
    public void Status_ConnectedAfterConnect()
    {
        if (!_brokerAvailable)
            return;

        _transport!.Status.Should().Be(TransportStatus.Connected);
    }
}
