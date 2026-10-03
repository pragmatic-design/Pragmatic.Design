using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Diagnostics;
using Pragmatic.Messaging.RequestReply;
using Pragmatic.Messaging.Routing;
using Pragmatic.Telemetry;
using static Pragmatic.Ensure.Ensure;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Messaging;

/// <summary>
///     IMessageBus implementation that delegates to an <see cref="IMessageTransport"/> for delivery
///     and uses <see cref="IMessageRouter"/> for topic/queue resolution.
///     PublishAsync serializes and sends via transport; DispatchAsync dispatches locally (for outbox delivery).
/// </summary>
public sealed partial class TransportAwareMessageBus : IMessageBus
{
    private readonly IMessageTransport _transport;
    private readonly IMessageRouter _router;
    private readonly IMessageSerializer _serializer;
    private readonly InMemoryMessageBus _localDispatcher;
    private readonly IIdempotencyStore? _idempotencyStore;
    private readonly ILogger<TransportAwareMessageBus> _logger;
    // One SG-generated resolver per module assembly ([PartitionKey] properties).
    private readonly IPartitionKeyResolver[] _partitionKeyResolvers;
    private readonly RequestReply.TransportReplyChannel? _replyChannel;
    private readonly IClaimCheckStore? _claimCheckStore;
    private readonly ClaimCheckOptions? _claimCheckOptions;
    private readonly TimeSpan _requestReplyTimeout;

    /// <summary>
    ///     How long a consumer's claim on a message id holds before another may take it over.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not a timeout on the handler — nothing here interrupts it. It is how long a message waits
    ///     after the process handling it is <b>killed</b>: until the lease expires, a redelivery sees a
    ///     claim that is still held and steps aside. Too short and two workers handle one message; too
    ///     long and a crash delays it.
    /// </remarks>
    private readonly TimeSpan _handlerLease;

    public TransportAwareMessageBus(
        IMessageTransport transport,
        IMessageRouter router,
        IMessageSerializer serializer,
        InMemoryMessageBus localDispatcher,
        ILogger<TransportAwareMessageBus> logger,
        // Optional: only consulted when EnableIdempotency() registered a store. Dedup is a
        // consume-side concern, so it is applied on DispatchAsync (transport → local handlers),
        // not on the producer PublishAsync paths.
        IIdempotencyStore? idempotencyStore = null,
        IEnumerable<IPartitionKeyResolver>? partitionKeyResolvers = null,
        RequestReply.TransportReplyChannel? replyChannel = null,
        IClaimCheckStore? claimCheckStore = null,
        ClaimCheckOptions? claimCheckOptions = null,
        MessagingOptions? options = null)
    {
        ThrowIfNull(transport);
        ThrowIfNull(router);
        ThrowIfNull(serializer);
        ThrowIfNull(localDispatcher);
        ThrowIfNull(logger);

        _transport = transport;
        _router = router;
        _serializer = serializer;
        _localDispatcher = localDispatcher;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
        _partitionKeyResolvers = partitionKeyResolvers is null ? [] : [.. partitionKeyResolvers];
        _replyChannel = replyChannel;
        _claimCheckStore = claimCheckStore;
        _claimCheckOptions = claimCheckOptions;
        _requestReplyTimeout = options?.RequestReplyTimeout ?? TimeSpan.FromSeconds(30);
        _handlerLease = options?.HandlerLease ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    ///     Claim check: payloads above the threshold move to the store and the message carries
    ///     only the reference header — the returned payload is an empty stub. No-op unless both
    ///     store and options are registered (EnableClaimCheck).
    /// </summary>
    private async Task<(byte[] Payload, MessageContext Context, string? ClaimCheckReference)> ApplyClaimCheckAsync(
        byte[] payload, MessageContext context, CancellationToken ct)
    {
        if (_claimCheckStore is null || _claimCheckOptions is null || payload.Length <= _claimCheckOptions.Threshold)
            return (payload, context, null);

        // Wrap the serialized bytes as a read-only stream (zero-copy) for the streaming store API.
        string reference;
        var stream = new MemoryStream(payload, writable: false);
        await using (stream.ConfigureAwait(false))
        {
            reference = await _claimCheckStore.StoreAsync(stream, ct).ConfigureAwait(false);
        }
        var headers = context.Headers is null
            ? new Dictionary<string, string>(1, StringComparer.Ordinal)
            : new Dictionary<string, string>(context.Headers, StringComparer.Ordinal);
        headers[IClaimCheckStore.HeaderName] = reference;

        MessagingDiagnostics.ClaimChecks.Add(1);
        LogClaimChecked(payload.Length, reference);
        return ([], context with { Headers = headers }, reference);
    }

    /// <summary>
    ///     Best-effort reclaim of a claim-check blob whose message failed to publish — otherwise the
    ///     offloaded payload is orphaned at rest (nothing consumes it, and DeleteAfterConsume defaults
    ///     to false). Uses <see cref="CancellationToken.None"/> so a cancelled publish still cleans up.
    /// </summary>
    private async Task TryReclaimClaimCheckAsync(string reference)
    {
        if (_claimCheckStore is null)
            return;

        try
        {
            await _claimCheckStore.DeleteAsync(reference, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Cleanup is best-effort: an orphaned blob costs storage, never correctness.
            LogClaimCheckReclaimFailed(reference, ex);
        }
    }

    /// <summary>
    ///     Stamps the message's <c>[PartitionKey]</c> value (when a generated resolver knows the
    ///     type) into the <see cref="IPartitionKeyResolver.HeaderName"/> header, which partitioned
    ///     transports (Kafka) use as the message key.
    /// </summary>
    private MessageContext StampPartitionKey(object message, MessageContext context)
    {
        foreach (var resolver in _partitionKeyResolvers)
        {
            if (resolver.TryGetPartitionKey(message) is { } key)
            {
                var headers = context.Headers is null
                    ? new Dictionary<string, string>(1, StringComparer.Ordinal)
                    : new Dictionary<string, string>(context.Headers, StringComparer.Ordinal);
                headers[IPartitionKeyResolver.HeaderName] = key;
                return context with { Headers = headers };
            }
        }

        return context;
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
        => PublishAsync(message, MessageContext.New(), ct);

    /// <inheritdoc />
    public async Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        // Stamp the ambient tenant so it travels on the wire and the consumer can restore it.
        context = _localDispatcher.EnrichTenant(context);

        var topic = _router.GetTopic<T>();
        var serialized = _serializer.Serialize(message, typeof(T));
        // The type travels with the message: the topic names the boundary, so the consumer cannot tell
        // from it what it is holding.
        var stamped = StampPartitionKey(
            message,
            context with { EnqueuedAt = DateTimeOffset.UtcNow, MessageType = typeof(T).FullName });
        var (payload, enrichedContext, claimCheckRef) = await ApplyClaimCheckAsync(serialized, stamped, ct).ConfigureAwait(false);

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity($"Publish.{typeof(T).Name}");
        activity?.SetTag(MessagingTags.MessageType, typeof(T).Name);
        activity?.SetTag(MessagingTags.Topic, topic);
        activity?.SetTag(MessagingTags.Transport, _transport.Name);

        MessagingDiagnostics.MessagesPublished.Add(1,
            new KeyValuePair<string, object?>("message.type", typeof(T).Name),
            new KeyValuePair<string, object?>("transport", _transport.Name));

        LogPublishing(typeof(T).Name, topic, _transport.Name, enrichedContext.MessageId);

        try
        {
            await _transport.PublishAsync(payload, topic, enrichedContext, ct).ConfigureAwait(false);
        }
        catch (Exception) when (claimCheckRef is not null)
        {
            // Publish failed after the payload was offloaded — reclaim the orphan blob, then surface.
            await TryReclaimClaimCheckAsync(claimCheckRef).ConfigureAwait(false);
            throw;
        }

        activity?.SetSuccess();
    }

    /// <inheritdoc />
    public async Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
    {
        // Local dispatch: used by consumer-side re-dispatch after the transport
        // has already delivered the payload. Producers (outbox / scheduled) must
        // use PublishAsync(object, Type, ...) to go through the transport.

        // At-least-once transports can redeliver the same MessageId. When a store is registered, the
        // id is claimed before the handlers run — it has to be this way round, because a handler that
        // has already run must not run again, which is the opposite of the producer side.
        //
        // ⚠️ A claim carries a state and a lease. With a bare test-and-set, a process killed
        // between the claim and the dispatch would leave a row that every redelivery reads as
        // "already handled": the message would be dropped without any handler ever running. So only
        // a COMPLETED claim drops a duplicate; one whose holder is gone is taken over once its lease
        // runs out.
        var idempotencyClaimed = false;
        if (_idempotencyStore is not null)
        {
            var claim = await _idempotencyStore
                .TryClaimAsync(context.MessageId, _handlerLease, ct)
                .ConfigureAwait(false);

            if (claim is not MessageClaim.Claimed)
            {
                MessagingDiagnostics.IdempotencyDuplicates.Add(1);
                LogDuplicateDropped(context.MessageId);
                return;
            }

            idempotencyClaimed = true;
        }

        try
        {
            await _localDispatcher.DispatchAsync(message, context, ct).ConfigureAwait(false);
        }
        catch when (idempotencyClaimed && _idempotencyStore is not null)
        {
            // Dispatch threw AFTER the id was claimed — either an infrastructure failure or (since
            // the W1 honest-outcome contract) a handler failure surfacing as AggregateException.
            // Release the claim so the transport redelivery re-handles instead of waiting out the
            // lease. ⚠️ No `is not OperationCanceledException` filter here, deliberately: a shutdown
            // during dispatch is precisely when the claim must not be left standing, and that filter
            // is what made the producer side lose messages.
            await _idempotencyStore.RemoveAsync(context.MessageId, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (idempotencyClaimed && _idempotencyStore is not null)
            await _idempotencyStore.MarkClaimCompletedAsync(context.MessageId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(context);

        // Stamp the ambient tenant so it travels on the wire and the consumer can restore it.
        context = _localDispatcher.EnrichTenant(context);

        var topic = _router.GetTopic(messageType);
        var serialized = _serializer.Serialize(message, messageType);
        // As above: what the topic cannot say, the context does.
        var stamped = StampPartitionKey(
            message,
            context with { EnqueuedAt = DateTimeOffset.UtcNow, MessageType = messageType.FullName });
        var (payload, enrichedContext, claimCheckRef) = await ApplyClaimCheckAsync(serialized, stamped, ct).ConfigureAwait(false);

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity($"Publish.{messageType.Name}");
        activity?.SetTag(MessagingTags.MessageType, messageType.Name);
        activity?.SetTag(MessagingTags.Topic, topic);
        activity?.SetTag(MessagingTags.Transport, _transport.Name);

        MessagingDiagnostics.MessagesPublished.Add(1,
            new KeyValuePair<string, object?>("message.type", messageType.Name),
            new KeyValuePair<string, object?>("transport", _transport.Name));

        LogPublishing(messageType.Name, topic, _transport.Name, enrichedContext.MessageId);

        try
        {
            await _transport.PublishAsync(payload, topic, enrichedContext, ct).ConfigureAwait(false);
        }
        catch (Exception) when (claimCheckRef is not null)
        {
            // Publish failed after the payload was offloaded — reclaim the orphan blob, then surface.
            await TryReclaimClaimCheckAsync(claimCheckRef).ConfigureAwait(false);
            throw;
        }

        activity?.SetSuccess();
    }

    /// <inheritdoc />
    public async Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
        => await SendAsync(message, MessageContext.New(), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        // SendAsync is point-to-point: the message goes to a dedicated queue
        // derived from the message type, not the handler type. Passing the
        // same type twice to GetQueue (old bug) collapsed handler+message
        // into message+message, producing wrong queue names in multi-handler
        // scenarios.
        var queue = _router.GetSendQueue(typeof(T));
        var payload = _serializer.Serialize(message, typeof(T));
        var enrichedContext = _localDispatcher.EnrichTenant(context) with { EnqueuedAt = DateTimeOffset.UtcNow };
        await _transport.SendAsync(payload, queue, enrichedContext, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : notnull
        where TResponse : notnull
    {
        // In-process handler wins (fast path, no broker roundtrip).
        try
        {
            return await _localDispatcher.RequestAsync<TRequest, TResponse>(request, ct).ConfigureAwait(false);
        }
        catch (RequestReply.NoLocalRequestHandlerException) when (_replyChannel is not null)
        {
            // No local IRequestHandler — go distributed over the transport. A business
            // InvalidOperationException from a real handler is NOT caught here (it propagates).
        }

        if (_replyChannel is null)
            throw new InvalidOperationException(
                $"No local IRequestHandler<{typeof(TRequest).Name}, {typeof(TResponse).Name}> and no " +
                "TransportReplyChannel registered for distributed request/reply.");

        return await RequestOverTransportAsync<TRequest, TResponse>(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Distributed request/reply: send the request point-to-point to its convention queue
    ///     with reply-to/request-id headers, then await the correlated reply on the process's
    ///     reply channel. Responder errors, timeouts and a transport that fails to carry the request
    ///     surface as <see cref="RequestReplyException"/>.
    /// </summary>
    /// <remarks>
    ///     A transport failure while the request is being sent (the reply channel cannot be opened, the
    ///     broker is unreachable, the channel is closed) is a request nobody answered, as a timeout is:
    ///     the caller cannot know what the responder would have said. What fails on this side of the wire
    ///     is not wrapped: serializing the request, deserializing the reply, a cancellation the caller
    ///     asked for.
    /// </remarks>
    private async Task<TResponse> RequestOverTransportAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : notnull
        where TResponse : notnull
    {
        var requestId = Guid.NewGuid().ToString("N");
        var queue = RequestReplyConventions.QueueFor(typeof(TRequest));
        var payload = _serializer.Serialize(request, typeof(TRequest));

        Task<(byte[] Payload, MessageContext Context)> replyTask;
        try
        {
            replyTask = await _replyChannel!.RegisterAsync(requestId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            throw NotSent(typeof(TRequest), queue, ex);
        }

        try
        {
            var context = MessageContext.New() with
            {
                Headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [TransportReplyChannel.ReplyToHeader] = _replyChannel.ReplyQueue,
                    [TransportReplyChannel.RequestIdHeader] = requestId,
                },
            };

            try
            {
                await _transport.SendAsync(payload, queue, context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                throw NotSent(typeof(TRequest), queue, ex);
            }

            var (responsePayload, responseContext) = await replyTask
                .WaitAsync(_requestReplyTimeout, ct).ConfigureAwait(false);

            if (responseContext.Headers is not null &&
                responseContext.Headers.TryGetValue(TransportReplyChannel.ErrorHeader, out var error))
            {
                throw new RequestReplyException($"Responder failed for {typeof(TRequest).Name}: {error}");
            }

            return (TResponse)_serializer.Deserialize(responsePayload, typeof(TResponse))!;
        }
        catch (TimeoutException)
        {
            throw new RequestReplyException(
                $"No reply for {typeof(TRequest).Name} within {_requestReplyTimeout} (queue: {queue}). " +
                "Is a responder running and bound to the request queue?");
        }
        finally
        {
            _replyChannel.Forget(requestId);
        }
    }

    private static bool IsCallerCancellation(Exception ex, CancellationToken ct)
        => ex is OperationCanceledException && ct.IsCancellationRequested;

    private static RequestReplyException NotSent(Type requestType, string queue, Exception cause)
        => new($"{requestType.Name} was not sent (queue: {queue}): the transport failed, so nobody answered it.", cause);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Publishing {MessageType} to topic {Topic} via {Transport} (id: {MessageId})")]
    private partial void LogPublishing(string messageType, string topic, string transport, string messageId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Dropped duplicate message (id: {MessageId}) — already processed (idempotency)")]
    private partial void LogDuplicateDropped(string messageId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Payload of {Bytes} bytes claim-checked to {Reference}")]
    private partial void LogClaimChecked(int bytes, string reference);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to reclaim claim-check blob {Reference} after a publish failure (orphaned — storage cost only)")]
    private partial void LogClaimCheckReclaimFailed(string reference, Exception ex);
}
