namespace Pragmatic.Messaging.Testing;

/// <summary>
///     Record of a message published via <see cref="MessageBusTestHarness"/>.
/// </summary>
public sealed record PublishedMessage(
    Type MessageType,
    object Message,
    MessageContext? Context,
    DateTimeOffset PublishedAt);
