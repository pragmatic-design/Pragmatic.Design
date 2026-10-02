namespace Pragmatic.ControlPlane;

/// <summary>
///     Runtime lifecycle state of a Pragmatic host.
/// </summary>
public enum HostState
{
    /// <summary>Host is initializing (DI, pipeline setup).</summary>
    Starting,

    /// <summary>Host is ready to serve requests.</summary>
    Ready,

    /// <summary>Host is executing database migrations.</summary>
    Migrating,

    /// <summary>Host is in maintenance mode (returns 503).</summary>
    Maintenance,

    /// <summary>
    ///     Host has left the rotation and is completing the requests it still has; new ones are no longer
    ///     routed to it.
    /// </summary>
    Draining,

    /// <summary>Host has stopped (only visible briefly before process exits).</summary>
    Stopped,

    /// <summary>
    ///     Host is out of the rotation with nothing in flight, and still running: it can be put back
    ///     (<see cref="ExitMaintenanceCommand" />) or stopped by a deploy.
    /// </summary>
    Drained,
}
