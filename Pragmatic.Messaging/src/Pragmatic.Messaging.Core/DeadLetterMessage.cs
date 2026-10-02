namespace Pragmatic.Messaging;

/// <summary>
///     Represents a message that failed all retry attempts and was moved to dead letter.
/// </summary>
/// <param name="MessageType">Fully qualified type name of the message.</param>
/// <param name="Payload">Serialized message payload (JSON).</param>
/// <param name="Error">Last error message.</param>
/// <param name="RetryCount">Total retry attempts made.</param>
/// <param name="Context">Original message context.</param>
/// <param name="FailedAt">Timestamp when the message was dead-lettered.</param>
public sealed record DeadLetterMessage(
    string MessageType,
    string Payload,
    string Error,
    int RetryCount,
    MessageContext Context,
    DateTimeOffset FailedAt)
{
    /// <summary>Stable identity for inspection/replay/removal (ops APIs).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}
