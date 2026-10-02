using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>App → Agent: report current lifecycle state.</summary>
public sealed class StateReportPayload
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; } // "ready", "maintenance", "draining", "stopped"
}
