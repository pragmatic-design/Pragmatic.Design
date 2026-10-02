namespace Pragmatic.ControlPlane;

/// <summary>
///     Classifies the role of a host in the Pragmatic topology.
/// </summary>
public enum HostType
{
    /// <summary>A standard tenant-facing application host.</summary>
    Tenant,

    /// <summary>A dedicated admin host (management, dashboards, tenant CRUD).</summary>
    Admin,

    /// <summary>A background worker (jobs, migrations, data processing).</summary>
    Worker,

    /// <summary>An API gateway or reverse proxy.</summary>
    Gateway,
}
