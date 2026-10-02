using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>Agent → App: state transition notification.</summary>
public sealed class StateChangePayload
{
    [JsonPropertyName("newState")]
    public required string NewState { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
