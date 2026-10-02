namespace Pragmatic.Messaging;

/// <summary>
///     Source-generated dispatch table that bridges untyped messages to typed PublishAsync&lt;T&gt;
///     via compile-time pattern matching. Eliminates MakeGenericMethod reflection at runtime.
/// </summary>
public interface ITypedMessageDispatchTable
{
    /// <summary>
    ///     Attempts to dispatch the message using a compile-time generated switch.
    ///     Returns null if the message type is not known to the SG (fallback to reflection).
    /// </summary>
    Task? TryDispatch(IMessageBus bus, object message, MessageContext context, CancellationToken ct);
}
