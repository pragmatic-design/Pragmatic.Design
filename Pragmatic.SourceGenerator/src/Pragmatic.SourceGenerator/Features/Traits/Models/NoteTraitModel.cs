using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
/// Model for note trait generation on an entity annotated with [HasNotes].
/// Simplified version of <see cref="CommentTraitModel"/> — no threading, no moderation.
/// </summary>
internal sealed record NoteTraitModel
{
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

    public required string ParentNamespace { get; init; }
    public required string ParentFullTypeName { get; init; }
    public required string IdType { get; init; }
    public string SimpleIdType => IdType.Contains('.') ? IdType.Substring(IdType.LastIndexOf('.') + 1) : IdType;


    public string? BoundaryFullTypeName { get; init; }
    public string? BoundaryName { get; init; }
    public string? ResourceSegment { get; init; }
    public string? ResourceParamName { get; init; }

    public int MaxLength { get; init; } = 4000;
    public bool AllowEditing { get; init; } = true;
    public int EditWindowMinutes { get; init; } = -1;
    public string? SubBoundaryOverride { get; init; }

    // ── Computed names ──────────────────────────────────────────────────

    public string SubBoundaryName => SubBoundaryOverride ?? $"{ParentTypeName}Notes";
    public string NoteTypeName => $"{ParentTypeName}Note";
    public string NoteFullTypeName => string.IsNullOrEmpty(ParentNamespace)
        ? NoteTypeName : $"{ParentNamespace}.{NoteTypeName}";
    public string ParentFkPropertyName => $"{ParentTypeName}Id";

    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
}
