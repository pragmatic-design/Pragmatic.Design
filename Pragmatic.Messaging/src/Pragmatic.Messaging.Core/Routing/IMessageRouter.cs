namespace Pragmatic.Messaging.Routing;

/// <summary>
///     Maps message types to transport topics/queues.
///     The SG generates a concrete implementation with compile-time switch expressions.
/// </summary>
public interface IMessageRouter
{
    /// <summary>Resolves the topic for publishing a message type.</summary>
    string GetTopic<T>() where T : notnull;

    /// <summary>Resolves the topic for publishing a message type (untyped).</summary>
    string GetTopic(Type messageType);

    /// <summary>
    ///     Resolves the point-to-point queue for <c>SendAsync</c> — addressed by message alone and
    ///     delivered to exactly one consumer.
    /// </summary>
    /// <remarks>
    ///     ⚠️ There is no per-handler queue method here: a <c>GetQueue(handlerType, messageType)</c> has
    ///     the wrong <b>signature</b> for the job. A subscription is per message type, not per handler —
    ///     <c>TransportSubscriptionBinder</c> subscribes once per type and dispatches in process to every
    ///     handler of it — so a name derived from a handler would need one queue per handler and
    ///     deliver each message to one of them. What a subscription name needs is the <em>subscriber</em>,
    ///     and that is <see cref="SubscriptionName" />.
    /// </remarks>
    string GetSendQueue(Type messageType) => $"send.{messageType.FullName ?? messageType.Name}";

    /// <summary>Resolves the named bus for a handler type. Null = default bus.</summary>
    string? GetBusName(Type handlerType) => null;
}
