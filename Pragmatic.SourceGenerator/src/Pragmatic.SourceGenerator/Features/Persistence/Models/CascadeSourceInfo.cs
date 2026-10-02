namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Identifies a property on an entity that is a cascade source.
///     When this property changes, an EntityPropertyChanged event should be emitted.
///     Collected from [CascadeSource] attributes (cross-project) and [CascadeOn] declarations (same-project).
/// </summary>
internal sealed record CascadeSourceInfo
{
    /// <summary>The FQN of the entity type (e.g., "global::Showcase.Catalog.Entities.RoomType").</summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>The property name that triggers cascades (e.g., "BaseRate").</summary>
    public required string PropertyName { get; init; }
}
