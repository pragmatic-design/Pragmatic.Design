using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Serialization;

/// <summary>
///     JSON wire format (default). Human-readable, good for debugging.
///     Uses System.Text.Json with camelCase and null-omitting.
/// </summary>
public sealed class JsonWireFormat : IWireFormat
{
    public static readonly JsonWireFormat Instance = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public string FormatName => "json";

    public byte[] Serialize<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, Options);

    public T? Deserialize<T>(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<T>(data, Options);

    /// <summary>Exposes the options for code that needs direct JsonSerializer access.</summary>
    public static JsonSerializerOptions SerializerOptions => Options;
}
