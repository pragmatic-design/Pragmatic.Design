namespace Pragmatic.Messaging.Entities;

/// <summary>
///     Resolves message types from their fully qualified name.
///     The SG generates an implementation with a switch expression for AOT safety.
/// </summary>
public interface IMessageTypeRegistry
{
    /// <summary>
    ///     Deserializes a message from its FQN and JSON payload.
    ///     Returns null if the type is not recognized.
    /// </summary>
    /// <param name="fullyQualifiedTypeName">The FQN stored in <see cref="OutboxMessage.MessageType"/>.</param>
    /// <param name="json">The JSON payload.</param>
    object? Deserialize(string fullyQualifiedTypeName, string json);
}
