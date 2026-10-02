using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
/// Model for tag trait generation on an entity annotated with [HasTags].
/// Built by <see cref="Transforms.TagTraitTransform"/>.
/// M:N relationship: parent entity ↔ Tag via junction entity.
/// </summary>
internal sealed record TagTraitModel
{
    // ── Parent entity info ──────────────────────────────────────────────

    /// <summary>Parent entity type name (e.g. "Reservation").</summary>
    public required string ParentTypeName { get; init; }

    /// <summary>
    ///     The parent is tenant-scoped (<c>ITenantEntity</c>).
    /// </summary>
    /// <remarks>
    ///     The child carries no tenant column of its own — its tenancy is the parent's — so the
    ///     generated list query, which filters by the parent's id alone, answered across tenants for
    ///     anyone holding a parent id. The DbContext lifts the parent's tenant filter onto the child
    ///     through the reference navigation. Not <c>ParentVisibilityFilter</c>: that one honours the
    ///     parent's <c>view-all</c> bypass, and no permission may cross a tenant.
    /// </remarks>
    public bool ParentIsTenantScoped { get; init; }

    /// <summary>The parent carries <c>[HasOwner]</c>.</summary>
    public bool ParentIsOwned { get; init; }

    /// <summary>The parent carries <c>[HasAccessScopes]</c>.</summary>
    public bool ParentIsScoped { get; init; }

    /// <summary>
    ///     Whether the parent restricts which rows a caller may see. The generated child list query
    ///     filters by the parent's id and nothing else, so the restriction is lifted onto the child
    ///     by <c>ParentVisibilityFilter</c> instead.
    /// </summary>
    public bool ParentIsRowProtected => ParentIsOwned || ParentIsScoped;


    /// <summary>Parent entity namespace.</summary>
    public required string ParentNamespace { get; init; }

    /// <summary>Parent entity fully qualified type name.</summary>
    public required string ParentFullTypeName { get; init; }

    /// <summary>Parent entity Id type (e.g. "System.Guid").</summary>
    public required string IdType { get; init; }

    /// <summary>Simple Id type for code gen (e.g. "Guid").</summary>
    public string SimpleIdType => IdType.Contains('.') ? IdType.Substring(IdType.LastIndexOf('.') + 1) : IdType;

    // ── Boundary info ───────────────────────────────────────────────────

    /// <summary>Boundary type full name (e.g. "Showcase.Booking.Booking").</summary>
    public string? BoundaryFullTypeName { get; init; }

    /// <summary>Boundary short name (e.g. "Booking").</summary>
    public string? BoundaryName { get; init; }

    // ── Resource info ───────────────────────────────────────────────────

    /// <summary>Resource segment from [Resource] (e.g. "reservations"). Null if no [Resource].</summary>
    public string? ResourceSegment { get; init; }

    /// <summary>Resource parameter name (e.g. "reservationId").</summary>
    public string? ResourceParamName { get; init; }

    // ── Tag options ─────────────────────────────────────────────────────

    /// <summary>Maximum tags per entity. 0 = unlimited. Default 50.</summary>
    public int MaxPerEntity { get; init; } = 50;

    /// <summary>Whether users can create new tags on the fly. Default true.</summary>
    public bool AllowCustom { get; init; } = true;

    /// <summary>Whether tag matching is case-sensitive. Default false.</summary>
    public bool CaseSensitive { get; init; }

    /// <summary>Tag scope. Null = parent entity type name as default scope.</summary>
    public string? ScopeOverride { get; init; }

    /// <summary>Override sub-boundary name. Default: {ParentTypeName}Tags.</summary>
    public string? SubBoundaryOverride { get; init; }

    // ── Computed names ──────────────────────────────────────────────────

    /// <summary>Effective tag scope (e.g. "Reservation").</summary>
    public string Scope => ScopeOverride ?? ParentTypeName;

    /// <summary>Sub-boundary name for grouping in boundary interface.</summary>
    public string SubBoundaryName => SubBoundaryOverride ?? $"{ParentTypeName}Tags";

    /// <summary>Tag entity type name shared per boundary (e.g. "BookingTag").</summary>
    public string TagTypeName => NamingHelper.AppendSuffix(BoundaryName ?? ParentTypeName, "Tag");

    /// <summary>Tag entity full type name.</summary>
    public string TagFullTypeName => string.IsNullOrEmpty(ParentNamespace)
        ? TagTypeName
        : $"{ParentNamespace}.{TagTypeName}";

    /// <summary>
    ///     Junction entity type name (e.g. "ReservationTagLink").
    ///     Suffixed with "TagLink" so it never collides with the shared per-boundary
    ///     <see cref="TagTypeName"/> — which it would when BoundaryName == ParentTypeName.
    /// </summary>
    public string JunctionTypeName => NamingHelper.AppendSuffix(ParentTypeName, "TagLink");

    /// <summary>Junction entity full type name.</summary>
    public string JunctionFullTypeName => string.IsNullOrEmpty(ParentNamespace)
        ? JunctionTypeName
        : $"{ParentNamespace}.{JunctionTypeName}";

    /// <summary>FK property name on junction → parent (e.g. "ReservationId").</summary>
    public string ParentFkPropertyName => $"{ParentTypeName}Id";

    /// <summary>
    ///     Read-side DTO type name (e.g. "ReservationTagDto"). Projected from the junction, so it
    ///     carries both the link audit (AddedAt/AddedBy) and the tag itself (Value/DisplayValue/Scope).
    /// </summary>
    public string TagDtoTypeName => NamingHelper.AppendSuffix(ParentTypeName, "TagDto");

    /// <summary>List query type name (e.g. "ListReservationTagsQuery").</summary>
    public string ListQueryTypeName => $"List{ParentTypeName}TagsQuery";

    /// <summary>Diagnostic location.</summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
}
