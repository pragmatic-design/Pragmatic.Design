namespace Pragmatic.Messaging.Testing;

/// <summary>A message successfully handled by a handler during a harness dispatch.</summary>
/// <param name="MessageType">Runtime type of the message.</param>
/// <param name="Message">The message instance.</param>
/// <param name="HandlerType">The handler that consumed it.</param>
/// <param name="Context">The message context (null for untracked paths).</param>
/// <param name="ConsumedAt">When the handler completed.</param>
public sealed record ConsumedMessage(
    Type MessageType,
    object Message,
    Type HandlerType,
    MessageContext? Context,
    DateTimeOffset ConsumedAt);
