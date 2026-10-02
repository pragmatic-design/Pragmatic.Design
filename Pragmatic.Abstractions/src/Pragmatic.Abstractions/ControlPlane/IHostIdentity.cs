namespace Pragmatic.ControlPlane;

/// <summary>
///     Singleton identity of a running Pragmatic host.
///     Registered automatically by the host bootstrap — every host has one.
/// </summary>
public interface IHostIdentity
{
    /// <summary>
    ///     Unique instance identifier (Guid7, regenerated on each startup).
    /// </summary>
    string HostId { get; }

    /// <summary>
    ///     Logical host name derived from the SG topology metadata
    ///     (e.g. "Showcase.Host", "Billing.Host").
    /// </summary>
    string HostName { get; }

    /// <summary>
    ///     Role of this host in the topology.
    /// </summary>
    HostType HostType { get; }

    /// <summary>
    ///     UTC timestamp when this host process started.
    /// </summary>
    DateTimeOffset StartedAt { get; }
}
