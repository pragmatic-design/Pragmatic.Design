namespace Pragmatic.Messaging;

/// <summary>
///     Handles messages of type <typeparamref name="T"/>.
///     Discovered by the source generator when decorated with <c>[MessageHandler]</c>.
/// </summary>
/// <typeparam name="T">The message type to handle.</typeparam>
public interface IMessageHandler<in T>
{
    /// <summary>
    ///     Execution order among handlers for the same message type.
    ///     Lower values execute first. Default is 0.
    /// </summary>
    int Order => 0;

    /// <summary>
    ///     Handles the message.
    /// </summary>
    /// <param name="message">The message payload.</param>
    /// <param name="context">Metadata about the message (correlation, tenant, retry count, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(T message, MessageContext context, CancellationToken ct = default);
}
