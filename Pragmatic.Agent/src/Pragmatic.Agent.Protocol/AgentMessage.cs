using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol;

/// <summary>
///     Wire format for all Agent socket communication.
///     Length-prefixed UTF-8 JSON: [4-byte big-endian length][JSON payload].
/// </summary>
public sealed class AgentMessage
{
    /// <summary>Message type discriminator.</summary>
    [JsonPropertyName("type")]
    public MessageType Type { get; init; }

    /// <summary>Correlation ID for request/response matching. Assigned by sender.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>JSON payload (type-specific). Deserialized lazily by the handler.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    /// <summary>Error message (only in Response type when success=false).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Whether the operation succeeded (only in Response type).</summary>
    [JsonPropertyName("success")]
    public bool? Success { get; init; }
}
