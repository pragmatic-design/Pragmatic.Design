using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.Outbox.Samples.Generated;
using Pragmatic.Messaging.RequestReply;
using Pragmatic.Messaging.Routing;
using ChannelOptions = Pragmatic.Messaging.Channels.ChannelOptions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     W5 distributed request/reply, end to end on GENERATED code: <c>[RequestHandler]</c> makes
///     the SG emit a typed <see cref="RequestSubscription"/> executor into
///     <c>AddPragmaticMessageHandlers()</c>; the transport consumer binds the request queue
///     (responder), and <c>IMessageBus.RequestAsync</c> goes over the transport with a
///     correlated reply channel when no local handler is registered. Responder errors surface
///     as <see cref="RequestReplyException"/> instead of a timeout.
/// </summary>
public static class DistributedRpcSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- SG distributed request/reply ([RequestHandler] over the transport) ---");

        // ── Responder process (owns the handler, binds the generated executor) ──
        var responderServices = new ServiceCollection();
        responderServices.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        responderServices.AddPragmaticMessageHandlers(); // generated: handler DI + RequestSubscription
        var responder = responderServices.BuildServiceProvider();

        var transport = new ChannelTransport(new ChannelOptions { Capacity = 100 }, NullLogger<ChannelTransport>.Instance);
        await transport.ConnectAsync();
        await RequestReplyBinder.BindAsync(
            transport,
            responder.GetRequiredService<IServiceScopeFactory>(),
            responder.GetServices<RequestSubscription>(),
            NullLogger.Instance);

        // ── Requester process (NO local handler → distributed path) ──
        var requesterServices = new ServiceCollection();
        requesterServices.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        var requester = requesterServices.BuildServiceProvider();

        var replyChannel = new TransportReplyChannel(transport, NullLogger<TransportReplyChannel>.Instance);
        var bus = new TransportAwareMessageBus(
            transport,
            new DefaultMessageRouter(),
            requester.GetRequiredService<IMessageSerializer>(),
            new InMemoryMessageBus(requester, NullLogger<InMemoryMessageBus>.Instance, requester.GetServices<ITypedMessageDispatchTable>()),
            NullLogger<TransportAwareMessageBus>.Instance,
            replyChannel: replyChannel);

        var quote = await bus.RequestAsync<GetFxQuote, FxQuote>(new GetFxQuote("EUR/USD"));
        Console.WriteLine($"  request queue            : {RequestReplyConventions.QueueFor(typeof(GetFxQuote))}");
        Console.WriteLine($"  reply received           : {quote.Pair} @ {quote.Rate}");

        var failed = false;
        try
        {
            await bus.RequestAsync<GetFxQuote, FxQuote>(new GetFxQuote("??/??"));
        }
        catch (RequestReplyException ex)
        {
            failed = ex.Message.Contains("unknown pair");
        }

        Console.WriteLine($"  responder error surfaced : {failed}");
        Console.WriteLine();

        await replyChannel.DisposeAsync();
        await transport.DisposeAsync();
    }
}

/// <summary>Request carrier for the RPC demo.</summary>
public sealed record GetFxQuote(string Pair);

/// <summary>Response carrier for the RPC demo.</summary>
public sealed record FxQuote(string Pair, decimal Rate);

/// <summary>
///     The responder. <c>[RequestHandler]</c> triggers SG emission of the typed request
///     executor (queue binding + deserialization + execution + response serialization).
/// </summary>
[RequestHandler]
public sealed class FxQuoteHandler : IRequestHandler<GetFxQuote, FxQuote>
{
    public Task<FxQuote> HandleAsync(GetFxQuote request, MessageContext context, CancellationToken ct)
        => request.Pair == "EUR/USD"
            ? Task.FromResult(new FxQuote(request.Pair, 1.0842m))
            : throw new InvalidOperationException($"unknown pair {request.Pair}");
}
