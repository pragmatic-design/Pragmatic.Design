namespace Pragmatic.Messaging.Testing;

/// <summary>A message whose handler threw during a harness dispatch.</summary>
/// <param name="MessageType">Runtime type of the message.</param>
/// <param name="Message">The message instance.</param>
/// <param name="HandlerType">The handler that faulted.</param>
/// <param name="Exception">The exception the handler threw.</param>
/// <param name="FaultedAt">When the failure was recorded.</param>
public sealed record FaultedMessage(
    Type MessageType,
    object Message,
    Type HandlerType,
    Exception Exception,
    DateTimeOffset FaultedAt);
