using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
/// Minimal info about a trait-generated entity, used to inject it into
/// DbContext generation (DbSet + EntityConfig application).
/// </summary>
internal sealed record TraitEntityInfo
{
    /// <summary>The trait entity type name (e.g. "ReservationComment").</summary>
    public required string TypeName { get; init; }

    /// <summary>The trait entity namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>Fully qualified type name.</summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>The boundary name this entity belongs to.</summary>
    public string? BoundaryName { get; init; }

    /// <summary>The trait type (e.g. "Comment", "Tag").</summary>
    public required string TraitKind { get; init; }

    /// <summary>
    /// Comma-separated primary-key column names, when the entity is not keyed on the surrogate
    /// <c>PersistenceId</c>. Set for the tag junction, whose key is (<c>{Parent}Id</c>, <c>TagId</c>);
    /// null for every trait entity that owns an Id.
    /// </summary>
    public string? KeyColumns { get; init; }

    /// <summary>
    ///     Whether the entity carries the soft-delete columns. False for the shared tag and for the tag
    ///     junction, whose base classes have no <c>IsDeleted</c>: claiming otherwise created three dead
    ///     columns and an index on a column no POCO maps.
    /// </summary>
    public bool IsSoftDelete { get; init; }

    /// <summary>
    ///     Relationships this entity has, so the emitted schema carries the same foreign keys and
    ///     indexes the generated EF configuration declares.
    ///     <para>
    ///     Without this the physical schema had neither: cascade delete existed only inside EF, a raw
    ///     <c>DELETE</c> left orphans, and every trait read was a sequential scan.
    ///     </para>
    /// </summary>
    public EquatableArray<TraitRelationInfo> Relations { get; init; } = EquatableArray<TraitRelationInfo>.Empty;

    /// <summary>
    ///     Columns forming a unique constraint, comma-separated. Set for the shared tag
    ///     (<c>Value</c>, <c>Scope</c>), whose de-duplication the documentation presents as a guarantee,
    ///     so the schema has to carry it and not only the EF model.
    /// </summary>
    public string? UniqueColumns { get; init; }

    /// <summary>
    ///     The child restricts its rows to those whose parent the caller may see, through the
    ///     generated <c>ParentVisibilityFilter</c>.
    /// </summary>
    /// <remarks>
    ///     Carried across the assembly boundary so the query-filter registration in the host can
    ///     register the filter. Without it the class is generated and never resolved, which is the
    ///     shape of defect this whole change exists to close.
    /// </remarks>
    public bool HasParentVisibilityFilter { get; init; }

    /// <summary>
    ///     The comment entity carries a generated <c>InternalVisibilityFilter</c>
    ///     (<c>[HasComments(SupportInternalNotes = true)]</c>). Carried across the assembly boundary for the
    ///     same reason as <see cref="HasParentVisibilityFilter" />: a filter nobody registers filters nothing.
    /// </summary>
    public bool HasInternalVisibilityFilter { get; init; }

    /// <summary>
    ///     The reference navigation to a tenant-scoped parent, or <c>null</c> when the parent is not
    ///     tenant-scoped or the entity has no parent at all (the shared tag).
    /// </summary>
    /// <remarks>
    ///     The child has no tenant column: its tenancy is its parent's, and the generated list query
    ///     filters by the parent's id alone. Carrying the navigation across the assembly boundary is
    ///     what lets the host's DbContext lift the parent's tenant filter onto the child — a caller
    ///     holding a parent id from another tenant read the whole thread otherwise.
    /// </remarks>
    public string? ParentTenantNavigation { get; init; }
}

/// <summary>
///     One foreign key of a trait entity: the column, the table it points at, and what happens to the
///     row when the referenced one is deleted.
/// </summary>
internal sealed record TraitRelationInfo
{
    /// <summary>The foreign-key column on this entity (e.g. <c>ReservationId</c>).</summary>
    public required string ForeignKeyColumn { get; init; }

    /// <summary>
    ///     The referenced entity type name, singular (e.g. <c>Reservation</c>). The schema transform
    ///     pluralizes it exactly as it does for hand-written entities, so both land on the same table.
    /// </summary>
    public required string ReferencedTypeName { get; init; }

    /// <summary>Delete behaviour: <c>Cascade</c> towards the parent, <c>Restrict</c> towards a tag.</summary>
    public required string OnDelete { get; init; }
}
