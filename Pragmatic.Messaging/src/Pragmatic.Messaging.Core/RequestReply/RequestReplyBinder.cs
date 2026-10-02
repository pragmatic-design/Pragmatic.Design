using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.RequestReply;

/// <summary>
///     The RESPONDER side of distributed request/reply: binds every registered
///     <see cref="RequestSubscription"/> (SG-generated typed executors) to the transport.
///     On each request: execute the handler in a fresh scope and send the serialized response
///     to the requester's reply queue; handler failures reply with <see cref="TransportReplyChannel.ErrorHeader"/>
///     so the requester fails fast instead of timing out.
/// </summary>
public static partial class RequestReplyBinder
{
    /// <summary>Binds all request subscriptions; returns handles for shutdown disposal.</summary>
    public static async Task<IReadOnlyList<IAsyncDisposable>> BindAsync(
        IMessageTransport transport,
        IServiceScopeFactory scopeFactory,
        IEnumerable<RequestSubscription> requestSubscriptions,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var handles = new List<IAsyncDisposable>();

        foreach (var subscription in requestSubscriptions)
        {
            var handle = await SubscribeQueueAsync(
                transport,
                subscription.Queue,
                async (payload, context, innerCt) =>
                {
                    var replyTo = GetHeader(context, TransportReplyChannel.ReplyToHeader);
                    var requestId = GetHeader(context, TransportReplyChannel.RequestIdHeader);
                    if (replyTo is null || requestId is null)
                    {
                        if (logger is not null)
                            LogMissingReplyHeaders(logger, subscription.Queue);
                        return;
                    }

                    var replyHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [TransportReplyChannel.RequestIdHeader] = requestId,
                    };

                    byte[] responsePayload;
                    try
                    {
                        var scope = scopeFactory.CreateAsyncScope();
                        await using (scope.ConfigureAwait(false))
                        {
                            responsePayload = await subscription
                                .Executor(scope.ServiceProvider, payload, context, innerCt)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (logger is not null)
                            LogRequestHandlerFailed(logger, subscription.Queue, ex);
                        replyHeaders[TransportReplyChannel.ErrorHeader] = ex.Message;
                        responsePayload = [];
                    }

                    var replyContext = MessageContext.New(correlationId: context.CorrelationId) with
                    {
                        Headers = replyHeaders,
                        TenantId = context.TenantId,
                    };
                    await transport.SendAsync(responsePayload, replyTo, replyContext, innerCt).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);

            handles.Add(handle);
            if (logger is not null)
                LogRequestBound(logger, subscription.RequestType.Name, subscription.Queue);
        }

        return handles;
    }

    private static string? GetHeader(MessageContext context, string key)
        => context.Headers is not null && context.Headers.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    ///     Queue consumption via the <see cref="IQueueConsumerTransport"/> capability when the
    ///     transport separates queues from topics (Azure Service Bus); fallback to
    ///     <see cref="IMessageTransport.SubscribeAsync"/> where send/subscribe share one address
    ///     space (Channels, RabbitMQ, Kafka, SQL).
    /// </summary>
    internal static Task<IAsyncDisposable> SubscribeQueueAsync(
        IMessageTransport transport,
        string queue,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct)
        => transport is IQueueConsumerTransport queueConsumer
            ? queueConsumer.SubscribeQueueAsync(queue, handler, ct)
            : transport.SubscribeAsync(queue, queue, handler, ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request handler bound: {RequestType} on {Queue}")]
    private static partial void LogRequestBound(ILogger logger, string requestType, string queue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request on {Queue} missing reply headers — dropped (not sent through IMessageBus.RequestAsync?)")]
    private static partial void LogMissingReplyHeaders(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request handler on {Queue} failed — error replied to requester")]
    private static partial void LogRequestHandlerFailed(ILogger logger, string queue, Exception ex);
}
