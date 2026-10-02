namespace Pragmatic.Agent.Protocol.Serialization;

/// <summary>
///     Pluggable wire format for Agent socket and gossip communication.
///     Default: JSON (human-readable, good for debugging).
///     Alternatives: MessagePack (fast, compact), Protobuf (cross-language).
/// </summary>
public interface IWireFormat
{
    /// <summary>Format identifier for negotiation. E.g., "json", "msgpack", "protobuf".</summary>
    string FormatName { get; }

    /// <summary>Serializes a message to bytes.</summary>
    byte[] Serialize<T>(T message);

    /// <summary>Deserializes a message from bytes.</summary>
    T? Deserialize<T>(ReadOnlySpan<byte> data);
}
