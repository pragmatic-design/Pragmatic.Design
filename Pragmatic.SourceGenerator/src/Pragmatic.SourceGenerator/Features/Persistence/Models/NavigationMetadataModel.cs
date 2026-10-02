namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents navigation metadata read from assembly attributes.
/// </summary>
internal sealed record NavigationMetadataModel
{
    /// <summary>
    ///     The navigation property name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The target entity type name.
    /// </summary>
    public required string TargetTypeName { get; init; }

    /// <summary>
    ///     The type of relationship (OneToMany, ManyToOne, OneToOne, ManyToMany).
    /// </summary>
    public required string NavigationType { get; init; }

    /// <summary>
    ///     The inverse navigation property name on the target entity.
    /// </summary>
    public string? InverseProperty { get; init; }

    /// <summary>
    ///     The foreign key property name.
    /// </summary>
    public string? ForeignKeyProperty { get; init; }

    /// <summary>
    ///     The delete behavior (NoAction, Cascade, SetNull, Restrict).
    /// </summary>
    public string OnDelete { get; init; } = "NoAction";

    /// <summary>
    ///     Whether this relationship is required.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     The join table name for ManyToMany.
    /// </summary>
    public string? JoinTable { get; init; }

    /// <summary>
    ///     Fully qualified type name of the target entity (e.g., "Showcase.Catalog.Entities.Property").
    /// </summary>
    public string? TargetFullTypeName { get; init; }

    /// <summary>
    ///     Fully qualified boundary type of the target entity (from its [BelongsTo&lt;T&gt;]).
    ///     Used to detect cross-boundary navigations.
    /// </summary>
    public string? TargetBoundaryTypeFullName { get; init; }

    /// <summary>
    ///     Whether this is a collection navigation.
    /// </summary>
    public bool IsCollection => NavigationType is "OneToMany" or "ManyToMany";

    /// <summary>
    ///     Whether this navigation was derived from a [Relation.*] attribute.
    /// </summary>
    public bool IsFromRelationAttribute { get; init; }

    /// <summary>
    ///     Whether this side is the principal (for OneToOne relationships).
    /// </summary>
    public bool IsPrincipal { get; init; }

    /// <summary>
    ///     The fully qualified join entity type name (for ManyToMany with explicit join entity).
    /// </summary>
    public string? JoinEntityTypeName { get; init; }

    /// <summary>The join entity's property holding this side's key.</summary>
    public string? JoinLeftKey { get; init; }

    /// <summary>The join entity's property holding the other side's key.</summary>
    public string? JoinRightKey { get; init; }

    /// <summary>
    ///     Whether this is an owned entity navigation (e.g., IdentityRecord via [UsePackage]).
    ///     Owned entities are mapped with <c>OwnsOne</c> (flat in parent table) instead of regular navigation.
    /// </summary>
    public bool IsOwned { get; init; }

    /// <summary>
    ///     The target is a <c>[Lookup]</c>: the key and its constraint are generated, but the reference
    ///     member is not — the lookup feature emits it, resolved from its cache and <c>[NotMapped]</c>.
    ///     The EF configuration therefore binds the key without a navigation.
    /// </summary>
    public bool IsLookupReference { get; init; }

    /// <summary>
    ///     Whether this navigation crosses a boundary (target entity belongs to a different boundary).
    ///     The table is then the other boundary's: no FK constraint and no EF configuration are
    ///     emitted for it, whether or not the source can read it.
    /// </summary>
    public bool IsCrossBoundary(string? sourceBoundary)
    {
        if (string.IsNullOrEmpty(sourceBoundary) || string.IsNullOrEmpty(TargetBoundaryTypeFullName))
            return false;
        return sourceBoundary != TargetBoundaryTypeFullName;
    }

    /// <summary>
    ///     Whether the navigation crosses a boundary the source cannot read: no CLR member exists for
    ///     it, only the key. A crossing the source's boundary declares <c>[ReadAccess&lt;T&gt;]</c> to
    ///     is a real, read-only navigation — the target sits in the same DbContext and EF joins it by
    ///     convention — so anything that loads through navigations must treat it as one.
    /// </summary>
    /// <remarks>
    ///     The same rule <c>RelationGraphBuilder.ProcessRelation</c> applies when it decides whether
    ///     to emit the member: a consumer that used the boundary comparison alone excluded a readable
    ///     navigation from every DTO include, silently.
    /// </remarks>
    public bool IsUnreadableCrossing(EntityMetadataModel source)
    {
        if (!IsCrossBoundary(source.BoundaryTypeFullName))
            return false;

        var target = TargetFullTypeName ?? TargetTypeName;
        foreach (var readable in source.ReadAccessTypes)
        {
            if (readable == target)
                return false;
        }

        return true;
    }
}
