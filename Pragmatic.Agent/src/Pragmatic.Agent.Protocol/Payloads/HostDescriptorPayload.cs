using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>
///     The host roster entry the daemon persists under <c>state/app:{appId}/{instanceId}</c>
///     (<see cref="HostRosterKeys" />) when an instance registers, and refreshes on each heartbeat. Gossip
///     replicates the key cluster-wide, so every connected host observes the full roster via
///     <c>KvPrefix("state/app:")</c> and the <c>state/app:</c> change stream.
/// </summary>
/// <remarks>
///     This is the wire shape read back by <c>AgentControlPlane.GetAllHostsAsync</c>; keep it in sync with
///     the fields that surface on <c>HostInfo</c>. Timestamps are UTC.
/// </remarks>
public sealed class HostDescriptorPayload
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("appName")]
    public required string AppName { get; init; }

    /// <summary>The running instance this entry lists — the last segment of its key.</summary>
    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Host role name — mirrors <c>Pragmatic.ControlPlane.HostType</c> (e.g. "Tenant", "Worker").</summary>
    [JsonPropertyName("hostType")]
    public string? HostType { get; init; }

    /// <summary>Lifecycle state name — mirrors <c>Pragmatic.ControlPlane.HostState</c> (e.g. "Ready").</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "Ready";

    [JsonPropertyName("processId")]
    public int ProcessId { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("lastHeartbeat")]
    public DateTimeOffset LastHeartbeat { get; init; }
}
