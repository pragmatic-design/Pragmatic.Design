namespace Pragmatic.Messaging.Channels;

/// <summary>
///     Internal envelope wrapping serialized payload + context for channel transport.
/// </summary>
internal readonly record struct ChannelEnvelope(
    ReadOnlyMemory<byte> Payload,
    MessageContext Context);
