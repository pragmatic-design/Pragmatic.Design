namespace Pragmatic.Agent.Client;

/// <summary>
///     Configuration options for connecting to the local Pragmatic Agent.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>
    ///     Path to the Agent Unix socket or Windows named pipe name.
    ///     Default: <c>/var/run/pragmatic/agent.sock</c> (Linux) or <c>pragmatic-agent</c> (Windows).
    /// </summary>
    public string SocketPath { get; set; } = OperatingSystem.IsWindows()
        ? "pragmatic-agent"
        : "/var/run/pragmatic/agent.sock";

    /// <summary>
    ///     Application ID for registration with the Agent.
    ///     Default: assembly name.
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    ///     Application name (human-readable).
    ///     Default: assembly name.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    ///     Heartbeat interval. Default: 15 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Whether to attempt reconnection if the Agent is unavailable.
    ///     Default: true (graceful degradation — app works without Agent).
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    ///     The route this host announces to the gateway through its Agent, or null to announce none.
    ///     Written each time the host registers, so a reconnected host announces itself again.
    /// </summary>
    public AgentRouteAnnouncement? Announce { get; set; }
}
