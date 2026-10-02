using System.Text;
using Azure.Messaging.ServiceBus;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.AzureServiceBus;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Azure Service Bus integration tests against the official emulator (Testcontainers).
///     Entities are pre-provisioned by the fixture's Config.json (the emulator has no
///     management API), with <c>AutoCreateEntities = false</c>.
/// </summary>
[Trait("Category", "Integration")]
[Collection("AzureServiceBusBroker")]
public class AzureServiceBusIntegrationTests(AzureServiceBusContainerFixture broker) : IAsyncLifetime
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    private AzureServiceBusTransport? _transport;
    private bool _brokerAvailable;

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        _brokerAvailable = broker.ConnectionString is not null;
        if (!_brokerAvailable)
            return;

        var options = new AzureServiceBusOptions
        {
            ConnectionString = broker.ConnectionString!,
            AutoCreateEntities = false, // emulator: entities come from Config.json
        };
        _transport = new AzureServiceBusTransport(options, NullLogger<AzureServiceBusTransport>.Instance);
        await _transport.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_transport is not null)
            await _transport.DisposeAsync();
    }
#pragma warning restore CA2007

    [Fact]
    public async Task PublishAndSubscribe_RoundTrip_WithHeaderPropagation()
    {
        if (!_brokerAvailable)
            return;

        var received = new TaskCompletionSource<(string Payload, MessageContext Context)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _transport!.SubscribeAsync(
            AzureServiceBusContainerFixture.Topic,
            AzureServiceBusContainerFixture.Subscription,
            (payload, ctx, _) =>
            {
                received.TrySetResult((Encoding.UTF8.GetString(payload.Span), ctx));
                return Task.CompletedTask;
            });

        var context = MessageContext.New(correlationId: "asb-test-1", tenantId: "acme") with
        {
            UserId = "user-7",
        };
        await _transport.PublishAsync("""{"orderId":"789"}"""u8.ToArray(), AzureServiceBusContainerFixture.Topic, context);

        var result = await received.Task.WaitAsync(ReceiveTimeout);

        result.Payload.Should().Contain("789");
        result.Context.CorrelationId.Should().Be("asb-test-1");
        result.Context.TenantId.Should().Be("acme");
        result.Context.UserId.Should().Be("user-7");
    }

    [Fact]
    public async Task SendAsync_PointToPoint_DeliversToQueue()
    {
        if (!_brokerAvailable)
            return;

        var payload = Encoding.UTF8.GetBytes("p2p-message");
        await _transport!.SendAsync(payload, AzureServiceBusContainerFixture.Queue, MessageContext.New());

        // Receive with a raw client: queue consumption is not part of the topic-based
        // subscription binder (documented v1 limitation).
        var client = new ServiceBusClient(broker.ConnectionString);
        await using (client.ConfigureAwait(true))
        {
            var receiver = client.CreateReceiver(AzureServiceBusContainerFixture.Queue);
            await using (receiver.ConfigureAwait(true))
            {
                var message = await receiver.ReceiveMessageAsync(ReceiveTimeout);

                message.Should().NotBeNull();
                message!.Body.ToString().Should().Be("p2p-message");
                await receiver.CompleteMessageAsync(message);
            }
        }
    }

    public sealed record AsbQuoteRequest(string Symbol);

    public sealed record AsbQuoteResponse(string Symbol, decimal Price);

    private sealed class AsbQuoteHandler : IRequestHandler<AsbQuoteRequest, AsbQuoteResponse>
    {
        public Task<AsbQuoteResponse> HandleAsync(AsbQuoteRequest request, MessageContext context, CancellationToken ct)
            => Task.FromResult(new AsbQuoteResponse(request.Symbol, 7.77m));
    }

    [Fact]
    public async Task RequestReply_Distributed_WorksOverQueues()
    {
        if (!_brokerAvailable)
            return;

        // Responder: typed executor bound on the REQUEST QUEUE via the p2p capability
        // (before IQueueConsumerTransport this bound a topic and never saw SendAsync traffic).
        var services = new ServiceCollection();
        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.AddScoped<IRequestHandler<AsbQuoteRequest, AsbQuoteResponse>, AsbQuoteHandler>();
        await using var sp = services.BuildServiceProvider();

        var subscription = new Pragmatic.Messaging.RequestReply.RequestSubscription(
            typeof(AsbQuoteRequest),
            Pragmatic.Messaging.RequestReply.RequestReplyConventions.QueueFor(typeof(AsbQuoteRequest)),
            static async (provider, payload, context, ct) =>
            {
                var serializer = provider.GetRequiredService<IMessageSerializer>();
                var request = (AsbQuoteRequest)serializer.Deserialize(payload, typeof(AsbQuoteRequest))!;
                var handler = provider.GetRequiredService<IRequestHandler<AsbQuoteRequest, AsbQuoteResponse>>();
                var response = await handler.HandleAsync(request, context, ct).ConfigureAwait(false);
                return serializer.Serialize(response, typeof(AsbQuoteResponse));
            });

        await Pragmatic.Messaging.RequestReply.RequestReplyBinder.BindAsync(
            _transport!, sp.GetRequiredService<IServiceScopeFactory>(), [subscription],
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        // Requester: reply channel on the PRE-PROVISIONED reply queue (emulator: no runtime entities).
        var replyChannel = new Pragmatic.Messaging.RequestReply.TransportReplyChannel(
            _transport!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Pragmatic.Messaging.RequestReply.TransportReplyChannel>.Instance,
            AzureServiceBusContainerFixture.ReplyQueue);

        await using var requesterSp = new ServiceCollection()
            .AddSingleton<IMessageSerializer, JsonMessageSerializer>()
            .BuildServiceProvider();
        var bus = new TransportAwareMessageBus(
            _transport!,
            new Pragmatic.Messaging.Routing.DefaultMessageRouter(),
            requesterSp.GetRequiredService<IMessageSerializer>(),
            new InMemoryMessageBus(requesterSp,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InMemoryMessageBus>.Instance,
                requesterSp.GetServices<ITypedMessageDispatchTable>()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TransportAwareMessageBus>.Instance,
            replyChannel: replyChannel);

        try
        {
            var response = await bus.RequestAsync<AsbQuoteRequest, AsbQuoteResponse>(new AsbQuoteRequest("MSFT"));

            response.Symbol.Should().Be("MSFT");
            response.Price.Should().Be(7.77m);
        }
        finally
        {
            await replyChannel.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailingHandler_IsAbandoned_AndRedeliveredWithDeliveryCount()
    {
        if (!_brokerAvailable)
            return;

        var attempts = 0;
        var succeeded = new TaskCompletionSource<MessageContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await _transport!.SubscribeAsync(
            AzureServiceBusContainerFixture.Topic,
            AzureServiceBusContainerFixture.Subscription,
            (_, ctx, _) =>
            {
                // Fail twice, then succeed: abandon → broker redelivery with growing DeliveryCount.
                if (Interlocked.Increment(ref attempts) <= 2)
                    throw new InvalidOperationException("transient");

                succeeded.TrySetResult(ctx);
                return Task.CompletedTask;
            });

        await _transport.PublishAsync("retry-me"u8.ToArray(), AzureServiceBusContainerFixture.Topic, MessageContext.New());

        var context = await succeeded.Task.WaitAsync(ReceiveTimeout);

        attempts.Should().Be(3);
        // DeliveryCount is broker truth: third delivery → RetryCount 2.
        context.RetryCount.Should().Be(2);
    }
}
