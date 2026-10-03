using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.RequestReply;
using Pragmatic.Messaging.Routing;
using ChannelOptions = Pragmatic.Messaging.Channels.ChannelOptions;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Distributed request/reply over a real transport (Channels — full transport path,
///     in-process): requester sends to the convention queue with reply-to/request-id headers,
///     the responder binder executes the typed executor and replies, the reply channel
///     correlates. Mirrors exactly what the SG emits per IRequestHandler.
/// </summary>
public class DistributedRequestReplyTests
{
    public sealed record QuoteRequest(string Symbol);

    public sealed record QuoteResponse(string Symbol, decimal Price);

    private sealed class QuoteHandler : IRequestHandler<QuoteRequest, QuoteResponse>
    {
        public Task<QuoteResponse> HandleAsync(QuoteRequest request, MessageContext context, CancellationToken ct)
            => request.Symbol == "BOOM"
                ? throw new InvalidOperationException("no such symbol")
                : Task.FromResult(new QuoteResponse(request.Symbol, 42.5m));
    }

    /// <summary>The subscription exactly as the SG emits it (typed lambda, zero reflection).</summary>
    private static RequestSubscription QuoteSubscription() => new(
        typeof(QuoteRequest),
        RequestReplyConventions.QueueFor(typeof(QuoteRequest)),
        static async (sp, payload, context, ct) =>
        {
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var request = (QuoteRequest)serializer.Deserialize(payload, typeof(QuoteRequest))!;
            var handler = sp.GetRequiredService<IRequestHandler<QuoteRequest, QuoteResponse>>();
            var response = await handler.HandleAsync(request, context, ct).ConfigureAwait(false);
            return serializer.Serialize(response, typeof(QuoteResponse));
        });

    [Fact]
    public async Task RequestAsync_ResponderThrows_SurfacesRequestReplyException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.AddScoped<IRequestHandler<QuoteRequest, QuoteResponse>, QuoteHandler>();
        await using var responderSp = services.BuildServiceProvider();

        var transport = new ChannelTransport(new ChannelOptions { Capacity = 100 }, NullLogger<ChannelTransport>.Instance);
        await transport.ConnectAsync();
        await RequestReplyBinder.BindAsync(
            transport, responderSp.GetRequiredService<IServiceScopeFactory>(), [QuoteSubscription()], NullLogger.Instance);

        // Requester with NO local handler (empty provider) → distributed path is the only path.
        await using var requesterSp = new ServiceCollection()
            .AddSingleton<IMessageSerializer, JsonMessageSerializer>()
            .BuildServiceProvider();
        var replyChannel = new TransportReplyChannel(transport, NullLogger<TransportReplyChannel>.Instance);
        var bus = new TransportAwareMessageBus(
            transport,
            new DefaultMessageRouter(),
            requesterSp.GetRequiredService<IMessageSerializer>(),
            new InMemoryMessageBus(requesterSp, NullLogger<InMemoryMessageBus>.Instance, requesterSp.GetServices<ITypedMessageDispatchTable>()),
            NullLogger<TransportAwareMessageBus>.Instance,
            replyChannel: replyChannel);

        try
        {
            var ok = await bus.RequestAsync<QuoteRequest, QuoteResponse>(new QuoteRequest("ACME"));
            ok.Price.Should().Be(42.5m);

            var boom = () => bus.RequestAsync<QuoteRequest, QuoteResponse>(new QuoteRequest("BOOM"));
            (await boom.Should().ThrowAsync<RequestReplyException>())
                .WithMessage("*no such symbol*");
        }
        finally
        {
            await replyChannel.DisposeAsync();
            await transport.DisposeAsync();
        }
    }

    /// <summary>
    ///     A broker that cannot be reached is a request nobody answered, as a timeout is: the caller does
    ///     not know whether the responder would have said yes, and is told so at once, with the transport's
    ///     reason kept as the inner exception.
    /// </summary>
    /// <remarks>
    ///     It used to surface as the transport's own <see cref="InvalidOperationException" />, which a caller
    ///     catching <see cref="RequestReplyException" /> does not see: the order example answered 500 where a
    ///     silent Stock answers 503.
    /// </remarks>
    [Fact]
    public async Task RequestAsync_BrokerUnreachable_SurfacesRequestReplyExceptionWithTheCause()
    {
        var transport = new Pragmatic.Messaging.RabbitMQ.RabbitMqTransport(
            new Pragmatic.Messaging.RabbitMQ.RabbitMqOptions { ConnectionString = "amqp://guest:guest@127.0.0.1:1/" },
            NullLogger<Pragmatic.Messaging.RabbitMQ.RabbitMqTransport>.Instance);
        var replyChannel = new TransportReplyChannel(transport, NullLogger<TransportReplyChannel>.Instance);
        var bus = RequesterOn(transport, replyChannel, out var requesterSp);

        try
        {
            var connect = () => transport.ConnectAsync();
            await connect.Should().ThrowAsync<Exception>();

            var request = () => bus.RequestAsync<QuoteRequest, QuoteResponse>(new QuoteRequest("ACME"));

            (await request.Should().ThrowAsync<RequestReplyException>())
                .WithInnerException<InvalidOperationException>();
        }
        finally
        {
            await replyChannel.DisposeAsync();
            await transport.DisposeAsync();
            await requesterSp.DisposeAsync();
        }
    }

    /// <summary>
    ///     A request its caller cancelled stays a cancellation: it is not the responder's silence, and
    ///     reporting it as one would answer 503 for a client that went away.
    /// </summary>
    [Fact]
    public async Task RequestAsync_CancelledByTheCaller_StaysACancellation()
    {
        var transport = new ChannelTransport(new ChannelOptions { Capacity = 100 }, NullLogger<ChannelTransport>.Instance);
        var replyChannel = new TransportReplyChannel(transport, NullLogger<TransportReplyChannel>.Instance);
        var bus = RequesterOn(transport, replyChannel, out var requesterSp);
        using var cancelled = new CancellationTokenSource();

        try
        {
            await transport.ConnectAsync();
            await cancelled.CancelAsync();
            var request = () => bus.RequestAsync<QuoteRequest, QuoteResponse>(new QuoteRequest("ACME"), cancelled.Token);

            await request.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            await replyChannel.DisposeAsync();
            await transport.DisposeAsync();
            await requesterSp.DisposeAsync();
        }
    }

    /// <summary>A requester with no local handler, so the transport is the only path.</summary>
    private static TransportAwareMessageBus RequesterOn(
        IMessageTransport transport, TransportReplyChannel replyChannel, out ServiceProvider requesterSp)
    {
        requesterSp = new ServiceCollection()
            .AddSingleton<IMessageSerializer, JsonMessageSerializer>()
            .BuildServiceProvider();
        return new TransportAwareMessageBus(
            transport,
            new DefaultMessageRouter(),
            requesterSp.GetRequiredService<IMessageSerializer>(),
            new InMemoryMessageBus(requesterSp, NullLogger<InMemoryMessageBus>.Instance, requesterSp.GetServices<ITypedMessageDispatchTable>()),
            NullLogger<TransportAwareMessageBus>.Instance,
            replyChannel: replyChannel);
    }
}
