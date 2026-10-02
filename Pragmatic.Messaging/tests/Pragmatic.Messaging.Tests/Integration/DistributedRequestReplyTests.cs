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
}
