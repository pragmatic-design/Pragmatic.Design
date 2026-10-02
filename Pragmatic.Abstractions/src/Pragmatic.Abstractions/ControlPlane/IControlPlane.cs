namespace Pragmatic.ControlPlane;

/// <summary>
///     Distributed control plane for coordinating Pragmatic hosts.
///     Default implementation is <c>NoOpControlPlane</c> (monolith mode — returns only self).
///     Replace with <c>AgentControlPlane</c> via <c>UseAgent()</c> for distributed mode (the Agent
///     KV + gossip backbone).
/// </summary>
public interface IControlPlane
{
    /// <summary>
    ///     Whether the control plane is connected to a remote hub.
    ///     False for <c>NoOpControlPlane</c> (monolith mode).
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    ///     Reports this host's current status (heartbeat).
    ///     Called automatically by the heartbeat service; also called on state transitions.
    /// </summary>
    Task ReportStatusAsync(CancellationToken ct = default);

    /// <summary>
    ///     Returns all hosts currently known to the control plane.
    ///     In monolith mode, returns only this host.
    /// </summary>
    Task<IReadOnlyList<HostInfo>> GetAllHostsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Streams real-time events from the control plane.
    ///     Events include state changes, migration progress, config changes, and deployment announcements.
    /// </summary>
    IAsyncEnumerable<ControlPlaneEvent> StreamEventsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Sends a command to a specific host.
    ///     Returns null on success, or a <see cref="ControlPlaneError"/> on failure.
    /// </summary>
    Task<ControlPlaneError?> SendCommandAsync(
        string targetHostId, HostCommand command, CancellationToken ct = default);

    /// <summary>
    ///     Broadcasts an event to all connected hosts.
    /// </summary>
    Task BroadcastEventAsync(ControlPlaneEvent evt, CancellationToken ct = default);
}
