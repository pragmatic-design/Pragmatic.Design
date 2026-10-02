using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>App → Agent: register this app instance.</summary>
public sealed class RegisterPayload
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("appName")]
    public required string AppName { get; init; }

    /// <summary>
    ///     This running instance — a host sends its <c>IHostIdentity.HostId</c>. Null → the daemon gives the
    ///     connection an id of its own. Two instances of one app are two roster entries.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public string? InstanceId { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("processId")]
    public int ProcessId { get; init; }

    /// <summary>Host role name — mirrors <c>Pragmatic.ControlPlane.HostType</c>. Null → daemon defaults to "Tenant".</summary>
    [JsonPropertyName("hostType")]
    public string? HostType { get; init; }

    /// <summary>App start time (UTC). Null → daemon stamps the first-register time.</summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; init; }
}
