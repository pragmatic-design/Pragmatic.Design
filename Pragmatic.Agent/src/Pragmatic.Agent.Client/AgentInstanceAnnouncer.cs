using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.ControlPlane;

namespace Pragmatic.Agent.Client;

/// <summary>
///     This running instance as the Agent knows it — the app it belongs to, its own id — and the route it
///     announces, in or out of the rotation.
/// </summary>
/// <remarks>
///     <para>
///         One per host, shared by the heartbeat (which announces when it registers) and the control plane
///         (which announces again when the host reports a new state). The announcement is ephemeral, under
///         this instance's own id: the Agent deletes it when this connection closes.
///     </para>
///     <para>
///         ⚠️ Leaving the rotation rewrites the announcement rather than deleting it: the gateway
///         keeps the route, so a service with every instance drained answers its maintenance status, not 404.
///     </para>
/// </remarks>
internal sealed class AgentInstanceAnnouncer(
    AgentConnection connection,
    AgentOptions options,
    IHostIdentity? identity = null,
    ILoggerFactory? loggerFactory = null)
{
    private readonly ILogger _logger = loggerFactory?.CreateLogger<AgentInstanceAnnouncer>() ?? NullLogger<AgentInstanceAnnouncer>.Instance;
    private readonly Lock _gate = new();
    private bool? _announcedInRotation;

    private static string EntryAssemblyName => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "app";

    /// <summary>
    ///     The app this instance registers under. The host's own name comes before the entry assembly's:
    ///     in-process hosts all share the runner as entry assembly, and each would register as "testhost".
    /// </summary>
    public string AppId { get; } = options.AppId ?? identity?.HostName ?? EntryAssemblyName;

    /// <summary>The app's display name.</summary>
    public string AppName { get; } = options.AppName ?? identity?.HostName ?? EntryAssemblyName;

    /// <summary>Distinct per process start, so two instances of one host are two entries and two destinations.</summary>
    public string InstanceId { get; } = identity?.HostId ?? Guid.NewGuid().ToString("N");

    /// <summary>
    ///     Whether an instance in <paramref name="state" /> takes new requests: not while it drains, is drained,
    ///     is in maintenance or has stopped.
    /// </summary>
    public static bool InRotation(HostState state)
        => state is not (HostState.Draining or HostState.Drained or HostState.Maintenance or HostState.Stopped);

    /// <summary>Announces the configured route, in or out of the rotation. No-op when the host announces none.</summary>
    public async Task AnnounceAsync(bool inRotation, CancellationToken ct = default)
    {
        if (options.Announce is not { } announce)
            return;

        var payload = new InstanceAnnouncementPayload
        {
            Address = announce.Address,
            Path = announce.Path,
            PathRemovePrefix = announce.PathRemovePrefix,
            RequireAuth = announce.RequireAuth,
            AppId = AppId,
            InRotation = inRotation,
        };

        var key = InstanceAnnouncementKeys.Key(announce.RouteId, InstanceId);
        var json = JsonSerializer.Serialize(payload, AgentClientJsonContext.Default.InstanceAnnouncementPayload);
        if (await connection.KvSetEphemeralAsync(key, json, ct).ConfigureAwait(false))
        {
            lock (_gate) _announcedInRotation = inRotation;
            _logger.LogInformation("Announced route {RouteId} at {Address} ({Rotation})", announce.RouteId,
                announce.Address, inRotation ? "in the rotation" : "out of the rotation");
        }
        else
        {
            _logger.LogWarning("Agent refused the announcement of route {RouteId}", announce.RouteId);
        }
    }

    /// <summary>Announces again only when the rotation <paramref name="state" /> implies differs from the last one announced.</summary>
    public Task RefreshAsync(HostState state, CancellationToken ct = default)
    {
        var inRotation = InRotation(state);
        lock (_gate)
        {
            if (_announcedInRotation is null || _announcedInRotation == inRotation)
                return Task.CompletedTask;
        }

        return AnnounceAsync(inRotation, ct);
    }
}
