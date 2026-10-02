// Pragmatic.Discovery - Module Deployment Info

namespace Pragmatic.Discovery.Models;

/// <summary>
/// Describes a module's deployment in a host, including which database it is assigned to.
/// Deserialized from the "includes" array in the HostTopology JSON.
/// </summary>
public sealed record ModuleDeploymentInfo
{
    /// <summary>The module class name (e.g., "BillingModule").</summary>
    public required string ModuleName { get; init; }

    /// <summary>The database class name, or null if the module has no database assignment.</summary>
    public string? DatabaseName { get; init; }

    /// <summary>The database provider (e.g., "SqlServer", "InMemory"), or null if not assigned.</summary>
    public string? Provider { get; init; }

    /// <summary>
    /// The configuration key that supplies this module's database connection string, or null if not
    /// declared. Parsed from the <c>configKey</c> field of the HostTopology JSON.
    /// </summary>
    public string? ConfigKey { get; init; }

    /// <summary>
    /// The generated DbContext class name for this module's database, or null if not assigned.
    /// Parsed from the <c>dbContext</c> field of the HostTopology JSON.
    /// </summary>
    public string? DbContext { get; init; }
}
