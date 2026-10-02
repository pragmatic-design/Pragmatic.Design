namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a [Relation.*] attribute declared on an entity.
///     Intermediate model between parsing and cross-entity wiring.
/// </summary>
internal sealed record RelationAttributeModel
{
    /// <summary>
    ///     The relation type: OneToOne, OneToMany, ManyToOne, ManyToMany.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    ///     The fully qualified target entity type name.
    /// </summary>
    public required string TargetTypeFullName { get; init; }

    /// <summary>
    ///     The short target entity type name (e.g., "LineItem").
    /// </summary>
    public required string TargetTypeName { get; init; }

    /// <summary>
    ///     The navigation property name. From WithNavigation("Name") or convention-derived.
    /// </summary>
    public string? NavigationName { get; init; }

    /// <summary>
    ///     The inverse navigation property name on the target entity.
    /// </summary>
    public string? InverseProperty { get; init; }

    /// <summary>
    ///     The foreign key property name. Explicit or convention-derived.
    /// </summary>
    public string? ForeignKeyProperty { get; init; }

    /// <summary>
    ///     The delete behavior for this relationship.
    /// </summary>
    public string OnDelete { get; init; } = "NoAction";

    /// <summary>
    ///     Whether this relationship is required.
    /// </summary>
    public bool IsRequired { get; init; } = true;

    /// <summary>
    ///     Whether this side is the principal (for OneToOne).
    /// </summary>
    public bool IsPrincipal { get; init; }

    /// <summary>
    ///     Custom join table name (for ManyToMany).
    /// </summary>
    public string? JoinTable { get; init; }

    /// <summary>
    ///     Fully qualified type name of the join entity (for ManyToMany with explicit join entity).
    /// </summary>
    public string? JoinEntityTypeName { get; init; }

    /// <summary>The join entity's property holding this side's key, from <c>LeftKey</c>.</summary>
    public string? JoinLeftKey { get; init; }

    /// <summary>The join entity's property holding the other side's key, from <c>RightKey</c>.</summary>
    public string? JoinRightKey { get; init; }

    /// <summary>
    ///     Fully qualified boundary type of the target entity.
    ///     Used for cross-boundary detection.
    /// </summary>
    public string? TargetBoundaryTypeFullName { get; init; }

    /// <summary>
    ///     Whether this was declared with explicit WithNavigation (vs convention).
    /// </summary>
    public bool IsExplicit { get; init; }
}
