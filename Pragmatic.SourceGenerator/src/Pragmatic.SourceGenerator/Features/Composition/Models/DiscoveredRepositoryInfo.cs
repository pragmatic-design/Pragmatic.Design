// Pragmatic.SourceGenerator - Composition - Discovered Repository Info
// Models for repositories discovered from referenced assembly metadata

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     A repository registration discovered from a referenced assembly's enriched Persistence metadata.
///     Contains the type info needed for direct DI registration by the host.
/// </summary>
internal sealed record DiscoveredRepositoryInfo
{
    /// <summary>Gets the fully qualified entity type name (e.g., "Showcase.Billing.Entities.Invoice").</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets the entity's ID type (e.g., "System.Guid", "int").</summary>
    public required string IdType { get; init; }

    /// <summary>Gets the fully qualified repository type name (e.g., "Showcase.Billing.Entities.InvoiceRepository").</summary>
    public required string RepositoryType { get; init; }

    /// <summary>Gets the boundary name this entity belongs to (e.g., "Billing").</summary>
    public string? BoundaryName { get; init; }

    /// <summary>Gets the source assembly name for grouping/comments.</summary>
    public required string SourceAssembly { get; init; }
}
