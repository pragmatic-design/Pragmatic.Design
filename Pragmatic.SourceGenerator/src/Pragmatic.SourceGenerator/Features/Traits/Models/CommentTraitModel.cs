using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
/// Model for comment trait generation on an entity annotated with [HasComments].
/// Built by <see cref="Transforms.CommentTraitTransform"/>.
/// </summary>
internal sealed record CommentTraitModel
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

    // ── Comment options ─────────────────────────────────────────────────

    /// <summary>Maximum content length.</summary>
    public int MaxLength { get; init; } = 2000;

    /// <summary>Whether replies (nested comments) are allowed.</summary>
    public bool AllowReplies { get; init; } = true;

    /// <summary>
    ///     Whether comment authors can edit their own comments.
    ///     When false the Update action and its endpoint are not generated at all.
    /// </summary>
    public bool AllowEditing { get; init; } = true;

    /// <summary>Edit window in minutes. -1 means no limit.</summary>
    public int EditWindowMinutes { get; init; } = -1;

    /// <summary>Whether new comments require moderator approval.</summary>
    public bool RequireApproval { get; init; }

    /// <summary>Whether internal/staff-only notes are supported.</summary>
    public bool SupportInternalNotes { get; init; }

    /// <summary>Override sub-boundary name in the boundary interface. Default: {ParentTypeName}Comments.</summary>
    public string? SubBoundaryOverride { get; init; }

    // ── Computed names ──────────────────────────────────────────────────

    /// <summary>Sub-boundary name for grouping in boundary interface.</summary>
    public string SubBoundaryName => SubBoundaryOverride ?? $"{ParentTypeName}Comments";

    /// <summary>Generated comment entity type name (e.g. "ReservationComment").</summary>
    public string CommentTypeName => $"{ParentTypeName}Comment";

    /// <summary>Generated comment entity full type name.</summary>
    public string CommentFullTypeName => string.IsNullOrEmpty(ParentNamespace)
        ? CommentTypeName
        : $"{ParentNamespace}.{CommentTypeName}";

    /// <summary>FK property name on the comment entity (e.g. "ReservationId").</summary>
    public string ParentFkPropertyName => $"{ParentTypeName}Id";

    /// <summary>Diagnostic location.</summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
}
