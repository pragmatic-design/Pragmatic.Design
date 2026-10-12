using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Agent.Protocol.Serialization;

/// <summary>
///     JSON wire format (default). Human-readable, good for debugging.
///     Uses System.Text.Json with camelCase and null-omitting.
/// </summary>
/// <remarks>
///     It writes the protocol's own types, <see cref="AgentMessage" /> and the payloads, through generated
///     metadata (<see cref="AgentWireJsonContext" />, <see cref="AgentPayloadJsonContext" />), not by reflection
///     (#142). Any other type throws <see cref="NotSupportedException" />.
/// </remarks>
public sealed class JsonWireFormat : IWireFormat
{
    public static readonly JsonWireFormat Instance = new();

    // The frame's options, with the payloads' metadata behind the frame's own: the frame type is written
    // camelCase, a payload type as the payload context declares it.
    private static readonly JsonSerializerOptions Options = new(AgentWireJsonContext.Default.Options)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(AgentWireJsonContext.Default, AgentPayloadJsonContext.Default),
    };

    public string FormatName => "json";

    public byte[] Serialize<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, Info<T>());

    public T? Deserialize<T>(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize(data, Info<T>());

    /// <summary>Exposes the options for code that needs direct JsonSerializer access.</summary>
    public static JsonSerializerOptions SerializerOptions => Options;

    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));
}
