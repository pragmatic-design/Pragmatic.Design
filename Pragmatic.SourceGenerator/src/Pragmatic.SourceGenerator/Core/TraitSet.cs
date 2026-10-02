namespace Pragmatic.SourceGenerator.Core;

/// <summary>
/// Immutable record describing which rich traits are applied to an entity.
/// Produced by <see cref="TraitDetector"/> from the entity's attribute set.
/// Each feature can call TraitDetector independently — zero coupling between features.
/// </summary>
internal sealed record TraitSet
{
    public bool IsAuditable { get; init; }
    public bool IsSoftDelete { get; init; }
    public bool IsSoftDeleteCascade { get; init; }
    public bool IsConcurrencyAware { get; init; }
    public bool IsMultiTenant { get; init; }
    public bool IsOwnedEntity { get; init; }
    public bool IsScopedEntity { get; init; }
    public bool HasComments { get; init; }
    public bool HasTags { get; init; }
    public bool HasNotes { get; init; }
    public bool HasAttachments { get; init; }

    /// <summary>No traits detected.</summary>
    public static TraitSet None { get; } = new();

    /// <summary>True if any rich trait (beyond persistence basics) is present. Extend when adding new rich traits.</summary>
    public bool HasAnyRichTrait => HasComments || HasTags || HasNotes || HasAttachments;
}




