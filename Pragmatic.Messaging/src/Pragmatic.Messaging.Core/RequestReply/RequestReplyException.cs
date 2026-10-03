namespace Pragmatic.Messaging.RequestReply;

/// <summary>
///     A distributed request failed: the responder reported an error, no reply arrived within the
///     timeout, or the transport failed to carry the request (its reason is the inner exception).
///     In each case the caller does not have the responder's answer.
/// </summary>
public sealed class RequestReplyException : Exception
{
    public RequestReplyException(string message) : base(message)
    {
    }

    public RequestReplyException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public RequestReplyException()
    {
    }
}
