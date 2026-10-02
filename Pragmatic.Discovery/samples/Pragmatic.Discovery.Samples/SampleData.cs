// Pragmatic.Discovery Samples - Shared sample topology builders.

using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Helpers that build realistic <see cref="HostTopologyInfo"/> instances used across the samples.
/// Mirrors the JSON shape the Composition source generator emits as
/// <c>[assembly: PragmaticMetadata(HostTopology, "...")]</c>.
/// </summary>
internal static class SampleData
{
    /// <summary>A monolith host that owns Catalog, Booking and Billing on a single database.</summary>
    public static HostTopologyInfo Monolith() => new()
    {
        HostName = "Showcase.Host",
        Modules =
        [
            new ModuleDeploymentInfo { ModuleName = "CatalogModule", DatabaseName = "ShowcaseDatabase", Provider = "PostgreSQL" },
            new ModuleDeploymentInfo { ModuleName = "BookingModule", DatabaseName = "ShowcaseDatabase", Provider = "PostgreSQL" },
            new ModuleDeploymentInfo { ModuleName = "BillingModule", DatabaseName = "ShowcaseDatabase", Provider = "PostgreSQL" },
        ],
        Boundaries =
        [
            new BoundaryReadAccessInfo { BoundaryName = "Booking", EntityTypes = ["CatalogProperty", "CatalogRoomType"] },
        ],
    };

    /// <summary>The main distributed host: owns Catalog + Booking only.</summary>
    public static HostTopologyInfo MainHost() => new()
    {
        HostName = "Showcase.Host",
        Modules =
        [
            new ModuleDeploymentInfo { ModuleName = "CatalogModule", DatabaseName = "ShowcaseDatabase", Provider = "PostgreSQL" },
            new ModuleDeploymentInfo { ModuleName = "BookingModule", DatabaseName = "ShowcaseDatabase", Provider = "PostgreSQL" },
        ],
        Boundaries =
        [
            new BoundaryReadAccessInfo { BoundaryName = "Booking", EntityTypes = ["CatalogProperty"] },
        ],
    };

    /// <summary>The standalone billing host: owns Billing on its own financial database.</summary>
    public static HostTopologyInfo BillingHost() => new()
    {
        HostName = "Showcase.Billing.Host",
        Modules =
        [
            new ModuleDeploymentInfo { ModuleName = "BillingModule", DatabaseName = "ShowcaseFinancialDatabase", Provider = "PostgreSQL" },
        ],
    };
}
