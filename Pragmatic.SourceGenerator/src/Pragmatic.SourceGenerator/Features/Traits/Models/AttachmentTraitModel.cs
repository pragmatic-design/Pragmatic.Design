using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
/// Model for attachment trait generation on an entity annotated with [HasAttachments].
/// 1:N file metadata with storage URI, upload/download/delete actions.
/// </summary>
internal sealed record AttachmentTraitModel
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

    public int MaxPerEntity { get; init; } = 20;
    public long MaxFileSizeBytes { get; init; } = 10_485_760;
    public string AllowedExtensions { get; init; } = "";
    public string? ContainerOverride { get; init; }
    public string? SubBoundaryOverride { get; init; }

    /// <summary>Retention window in days for soft-deleted attachments. 0 / negative = no purge job.</summary>
    public int PurgeDeletedAfterDays { get; init; }

    /// <summary>Cron schedule of the generated purge job.</summary>
    public string PurgeCron { get; init; } = "0 3 * * *";

    /// <summary>Bounds of the derived thumbnail. Either at 0 means no thumbnail.</summary>
    public int ThumbnailMaxWidth { get; init; }

    /// <inheritdoc cref="ThumbnailMaxWidth" />
    public int ThumbnailMaxHeight { get; init; }

    // ── Computed ────────────────────────────────────────────────────────

    /// <summary>
    ///     Whether the purge job must be generated. The default (0) generates nothing, so an existing
    ///     consumer never gets a background job that starts deleting its data.
    /// </summary>
    public bool PurgeEnabled => PurgeDeletedAfterDays > 0;

    /// <summary>
    ///     The schedule actually used. An empty or blank <see cref="PurgeCron"/> falls back to the
    ///     documented default, which keeps the intent: taken literally, it would disable the whole
    ///     retention feature without a word, and a developer who set PurgeDeletedAfterDays would get no
    ///     job and no way to know.
    /// </summary>
    public string EffectivePurgeCron =>
        string.IsNullOrWhiteSpace(PurgeCron) ? "0 3 * * *" : PurgeCron;

    /// <summary>
    ///     Whether the attribute asked for a thumbnail. Both bounds, because one of them is a half
    ///     configuration and half a configuration is the shape that reads as on and behaves as off.
    /// </summary>
    public bool ThumbnailRequested => ThumbnailMaxWidth > 0 && ThumbnailMaxHeight > 0;

    public string PurgeJobTypeName => $"Purge{ParentTypeName}AttachmentsJob";

    public string SubBoundaryName => SubBoundaryOverride ?? $"{ParentTypeName}Attachments";
    public string AttachmentTypeName => $"{ParentTypeName}Attachment";
    public string AttachmentFullTypeName => string.IsNullOrEmpty(ParentNamespace)
        ? AttachmentTypeName : $"{ParentNamespace}.{AttachmentTypeName}";
    public string ParentFkPropertyName => $"{ParentTypeName}Id";
    public string Container => ContainerOverride ?? ParentTypeName.ToLowerInvariant();

    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
}
