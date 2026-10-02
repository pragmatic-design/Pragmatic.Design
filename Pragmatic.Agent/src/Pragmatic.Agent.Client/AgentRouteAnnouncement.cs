namespace Pragmatic.Agent.Client;

/// <summary>
///     The route this host serves behind the gateway, announced through its Agent: every Agent in the
///     cluster learns it, and a gateway builds its route and its cluster of instances from the announcements.
/// </summary>
/// <remarks>
///     Configured under <c>Pragmatic:Agent:Announce</c>. The announcement lives as long as the host's
///     connection to its Agent, so an instance that stops leaves the gateway's rotation.
/// </remarks>
public sealed class AgentRouteAnnouncement
{
    /// <summary>The route id — the same for every instance of the service, and the gateway's cluster id.</summary>
    public required string RouteId { get; init; }

    /// <summary>The route pattern the gateway matches, e.g. <c>/warehouse/{**catch-all}</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The prefix removed before forwarding. Null forwards the path as it came.</summary>
    public string? PathRemovePrefix { get; init; }

    /// <summary>Whether the gateway admits only an authenticated caller.</summary>
    public bool RequireAuth { get; init; }

    /// <summary>
    ///     The address the gateway reaches this instance on. Configured, not read from the server: behind
    ///     a container network or a load balancer the address a host listens on is not the one it is reached at.
    /// </summary>
    public required string Address { get; init; }
}
