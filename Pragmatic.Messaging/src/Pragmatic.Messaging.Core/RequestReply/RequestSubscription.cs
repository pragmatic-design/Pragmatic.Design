namespace Pragmatic.Messaging.RequestReply;

/// <summary>
///     A responder-side binding for one <c>IRequestHandler&lt;TReq, TRes&gt;</c>: the queue to
///     consume and a TYPED executor (SG-generated lambda — zero reflection) that deserializes
///     the request, runs the handler in the given scope, and returns the serialized response.
/// </summary>
/// <param name="RequestType">The request CLR type (diagnostics/labels).</param>
/// <param name="Queue">The point-to-point queue requests arrive on (see <see cref="RequestReplyConventions"/>).</param>
/// <param name="Executor">Deserializes, executes the handler, serializes the response.</param>
public sealed record RequestSubscription(
    Type RequestType,
    string Queue,
    Func<IServiceProvider, ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task<byte[]>> Executor);
