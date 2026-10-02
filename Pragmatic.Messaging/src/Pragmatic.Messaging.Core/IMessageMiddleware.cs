namespace Pragmatic.Messaging;

/// <summary>
///     Middleware that wraps message handler execution.
///     Applied globally or per message type via <c>[MessageMiddleware]</c>.
/// </summary>
public interface IMessageMiddleware
{
    /// <summary>
    ///     Execution order among middleware. Lower values execute first (outermost).
    /// </summary>
    int Order => 0;

    /// <summary>
    ///     Invokes the middleware. Call <paramref name="next"/> to continue the pipeline.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message payload.</param>
    /// <param name="context">Message metadata.</param>
    /// <param name="next">Delegate to invoke the next middleware or handler.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvokeAsync<T>(T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
        where T : notnull;
}
