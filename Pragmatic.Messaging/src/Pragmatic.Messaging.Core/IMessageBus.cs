namespace Pragmatic.Messaging;

/// <summary>
///     Publishes messages to registered handlers.
///     In-memory implementation dispatches directly; outbox-backed implementation
///     persists first and delivers asynchronously.
/// </summary>
/// <remarks>
///     Declared as provided by the host: <c>UseMessaging()</c> registers the implementation, and which
///     one depends on the transport the host chose. Without the declaration, any package whose own
///     type injects it is refused <c>PRAG1641</c> on correct code — which is how the messaging bridge's
///     job met it.
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IMessageBus
{
    /// <summary>
    ///     Publishes a typed message to all registered handlers.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull;

    /// <summary>
    ///     Publishes a typed message with explicit context.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message payload.</param>
    /// <param name="context">Message metadata (correlation, tenant, headers).</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull;

    /// <summary>
    ///     Dispatches an untyped message object to in-process handlers only.
    ///     Bypasses any configured transport — use <see cref="PublishAsync(object, Type, MessageContext, CancellationToken)"/>
    ///     when you need transport-backed delivery. Intended for consumer-side re-dispatch after the
    ///     transport has already delivered the payload.
    /// </summary>
    /// <param name="message">The message object.</param>
    /// <param name="context">Message metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default);

    /// <summary>
    ///     Publishes an untyped message through the active bus/transport.
    ///     Use this from outbox delivery and scheduled-message jobs — these flows
    ///     resolve the message type at runtime but must still leave the process
    ///     when a transport is configured.
    /// </summary>
    Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default);

    /// <summary>
    ///     Sends a point-to-point message to a single consumer (queue semantics).
    ///     Unlike PublishAsync (fan-out), only one handler receives the message.
    /// </summary>
    Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull;

    /// <summary>
    ///     Sends a point-to-point message with explicit context.
    /// </summary>
    Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull;

    /// <summary>
    ///     Sends a request and waits for a typed response (request-reply pattern).
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : notnull
        where TResponse : notnull;
}
