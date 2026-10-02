// Pragmatic.Discovery - Boundary ReadAccess Info

namespace Pragmatic.Discovery.Models;

/// <summary>
/// Describes the ReadAccess declarations for a boundary.
/// Deserialized from the "boundaries" array in the HostTopology JSON.
/// </summary>
public sealed record BoundaryReadAccessInfo
{
    /// <summary>The boundary name (e.g., "Booking").</summary>
    public required string BoundaryName { get; init; }

    /// <summary>
    /// Entity type names this boundary needs to read via SQL join
    /// (declared with [ReadAccess&lt;T&gt;] on the boundary class).
    /// </summary>
    public IReadOnlyList<string> EntityTypes { get; init; } = [];
}
