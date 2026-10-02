namespace Pragmatic.Messaging;

/// <summary>
///     Serializes and deserializes messages.
///     Dual API: string for outbox persistence, byte[] for transport.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    ///     Serializes a message to a JSON string (for outbox persistence).
    /// </summary>
    string SerializeToString<T>(T message) where T : notnull;

    /// <summary>
    ///     Deserializes a message from a JSON string.
    /// </summary>
    T? DeserializeFromString<T>(string json) where T : notnull;

    /// <summary>
    ///     Serializes a message to bytes (for transport).
    /// </summary>
    byte[] Serialize<T>(T message) where T : notnull;

    /// <summary>
    ///     Deserializes a message from bytes.
    /// </summary>
    T? Deserialize<T>(byte[] data) where T : notnull;

    // Untyped overloads for transport consumers (type resolved at runtime from headers)

    /// <summary>
    ///     Serializes a message to bytes (untyped, for transport layer).
    /// </summary>
    byte[] Serialize(object message, Type type);

    /// <summary>
    ///     Deserializes a message from bytes (untyped, for transport consumers).
    /// </summary>
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);

    /// <summary>
    ///     Deserializes a message directly from a stream (untyped), reading it incrementally so a large
    ///     claim-checked payload is never fully buffered. The caller owns and disposes the stream.
    /// </summary>
    ValueTask<object?> DeserializeAsync(Stream data, Type type, CancellationToken ct = default);
}
