namespace Pragmatic.Messaging.RequestReply;

/// <summary>
///     A distributed request failed: the responder reported an error, or no reply arrived
///     within the timeout.
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
