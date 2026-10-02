using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>App → Agent: periodic health heartbeat.</summary>
public sealed class HeartbeatPayload
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("health")]
    public required string Health { get; init; } // "healthy", "degraded", "unhealthy"

    /// <summary>
    ///     Current host lifecycle state name (mirrors <c>Pragmatic.ControlPlane.HostState</c>, e.g.
    ///     "Ready"/"Maintenance"/"Draining"). Null when the app does not report lifecycle state; the
    ///     daemon then leaves the roster descriptor's state unchanged.
    /// </summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }
}
