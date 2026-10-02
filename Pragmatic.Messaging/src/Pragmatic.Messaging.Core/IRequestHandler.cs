namespace Pragmatic.Messaging;

/// <summary>
///     Handles request-reply messages. The handler processes a request
///     and returns a typed response.
/// </summary>
/// <typeparam name="TRequest">The request message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    /// <summary>
    ///     Handles the request and returns a response.
    /// </summary>
    Task<TResponse> HandleAsync(TRequest request, MessageContext context, CancellationToken ct = default);
}
