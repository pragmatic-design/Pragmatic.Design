namespace Pragmatic.Messaging;

/// <summary>
///     Marker registered in DI — one per message type that has a handler — so transport consumer
///     services know which message types to subscribe to at startup, without reflection. The
///     source generator emits one <c>AddMessageSubscription&lt;T&gt;()</c> call per message type from
///     <c>AddPragmaticMessageHandlers</c>; the active transport's consumer service injects
///     <see cref="System.Collections.Generic.IEnumerable{T}"/> of these and binds each one.
/// </summary>
public sealed class MessageSubscription(Type messageType, string subscriber, string? busName = null)
{
    /// <summary>The CLR type of the message to subscribe to.</summary>
    public Type MessageType { get; } = Ensure.Ensure.ThrowIfNull(messageType);

    /// <summary>
    ///     Who is listening — the module that registered this subscription, as the name it should be
    ///     known by at the broker.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ This is what makes two services consuming one event each get a copy instead of
    ///         dividing the messages between them. The subscription name was built from the transport
    ///         and the message type alone, so two services subscribing to one event declared the same
    ///         queue on one broker and became competing consumers on it.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Required, and that is the point.</b> A subscription that does not say who is
    ///         listening cannot be given a name that distinguishes it, and every fallback available at
    ///         that moment — the transport, the message, the machine — is the defect. The generator
    ///         fills it from the module being compiled;
    ///         <see cref="Configuration.MessagingOptions.SubscriberName" /> overrides it where a
    ///         deployment names its own consumer group.
    ///     </para>
    /// </remarks>
    public string Subscriber { get; } = Ensure.Ensure.ThrowIfNullOrWhiteSpace(subscriber);

    /// <summary>Named bus this subscription belongs to ([OnBus] handlers); null = default bus.</summary>
    public string? BusName { get; } = busName;
}
