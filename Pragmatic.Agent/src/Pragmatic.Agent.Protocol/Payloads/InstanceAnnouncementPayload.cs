using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>
///     What one running instance of a service says about itself to the gateway: the route it serves and
///     the address it serves it on. Written by the host under <see cref="InstanceAnnouncementKeys.Key" />
///     as an <b>ephemeral</b> entry, so it leaves the cluster when the instance does.
/// </summary>
/// <remarks>
///     <para>
///         One key per instance, never one per route: every instance writing the same route key would make
///         the last writer's disconnect delete it for the ones still running. The gateway derives a route,
///         and its cluster of destinations, from the instances announcing it.
///     </para>
///     <para>
///         Every instance of a route announces the same <see cref="Path" />, <see cref="PathRemovePrefix" />
///         and <see cref="RequireAuth" />; they are the service's, not the instance's.
///     </para>
/// </remarks>
public sealed class InstanceAnnouncementPayload
{
    /// <summary>The address the gateway forwards to, e.g. <c>http://10.0.0.5:8080</c>.</summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }

    /// <summary>The route pattern the gateway matches, e.g. <c>/warehouse/{**catch-all}</c>.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>The prefix removed before forwarding, so the service answers on its own paths.</summary>
    [JsonPropertyName("pathRemovePrefix")]
    public string? PathRemovePrefix { get; init; }

    /// <summary>Whether the gateway admits only an authenticated caller on this route.</summary>
    [JsonPropertyName("requireAuth")]
    public bool RequireAuth { get; init; }

    /// <summary>The app id the instance registered under — the roster entry that says whether it is in maintenance.</summary>
    [JsonPropertyName("appId")]
    public string? AppId { get; init; }

    /// <summary>
    ///     Whether the gateway sends new requests to this instance. False while it drains or is drained: the
    ///     route stays, and the instance is not one of its destinations.
    /// </summary>
    /// <remarks>
    ///     The announcement stays when the instance leaves the rotation, rather than being deleted: with every
    ///     instance of a service drained, the route still exists and the gateway answers it with the
    ///     service's maintenance status, where a deleted route would answer 404.
    /// </remarks>
    [JsonPropertyName("inRotation")]
    public bool InRotation { get; init; } = true;
}
