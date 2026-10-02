using Pragmatic.Messaging.Configuration;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Messaging.Routing;

/// <summary>
///     Builds the name a consumer subscribes under — which on most brokers <b>is</b> the queue name,
///     so it is the thing that decides whether two services each get a copy of an event or divide it
///     between them.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It names the subscriber. A name like <c>$"{transport.Name}-{messageType.Name}"</c> names
///         the transport and the message and nothing of the subscriber: two services subscribing to one
///         event would both declare <c>rabbitmq-VerificationRequested</c> on the same broker, become
///         competing consumers on one queue, and each request would reach one of them — the opposite of
///         what a topic is for. A saga waiting for it would never start.
///     </para>
///     <para>
///         The subscriber is the unit that matches how subscriptions are bound:
///         <c>TransportSubscriptionBinder</c> deduplicates by <b>message type</b> and subscribes once,
///         then dispatches in process to every handler of that type. So the name cannot be per handler
///         — that would undo the deduplication and deliver one message to one handler instead of all —
///         and it cannot be per boundary either, for the same reason when one module hosts several. It
///         is per module, per message type, per bus.
///     </para>
///     <para>
///         A name is <b>operational state</b>: renaming it on a running deployment leaves the old queue
///         bound and holding messages. It is a pure function of its three arguments for that reason,
///         and <see cref="MessagingOptions.SubscriberName" /> exists so a deployment can pin it rather
///         than inherit whatever the module is called.
///     </para>
/// </remarks>
public static class SubscriptionName
{
    /// <summary>
    ///     The name for one subscriber's subscription to one message type on one bus.
    /// </summary>
    /// <param name="subscriber">
    ///     Who is listening — the module that registered the subscription, or the name a deployment
    ///     pinned. Required: an empty one collapses every service back onto a single queue, which is
    ///     the defect this exists to close.
    /// </param>
    /// <param name="messageType">The message type subscribed to.</param>
    /// <param name="busName">The named bus, or <c>null</c> for the default one.</param>
    public static string For(string subscriber, Type messageType, string? busName)
    {
        ThrowIfNull(messageType);
        ThrowIfNullOrWhiteSpace(subscriber);

        var message = MessagingNames.ToKebabCase(messageType.Name);

        // The default bus adds no segment: it is the bus every ordinary application has, and a
        // placeholder for it would put a segment that never varies on every queue in existence.
        return string.IsNullOrWhiteSpace(busName)
            ? $"{subscriber}.{message}"
            : $"{subscriber}.{busName}.{message}";
    }
}
