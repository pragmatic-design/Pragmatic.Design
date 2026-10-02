namespace Pragmatic.Messaging.RequestReply;

/// <summary>
///     Signals that no in-process <see cref="IRequestHandler{TRequest,TResponse}"/> is registered for a
///     request. It is a control-flow sentinel: <see cref="TransportAwareMessageBus"/> catches exactly this
///     type to fall back to distributed request/reply, so a business
///     <see cref="System.InvalidOperationException"/> thrown by a real handler is never mistaken for
///     "no handler" and re-routed over the transport.
/// </summary>
/// <remarks>
///     Derives from <see cref="System.InvalidOperationException"/> for backward compatibility with callers
///     that already catch that type for the missing-handler case.
/// </remarks>
public sealed class NoLocalRequestHandlerException : InvalidOperationException
{
    public NoLocalRequestHandlerException(string message) : base(message) { }
}
