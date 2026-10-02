using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Manifest.Models;

/// <summary>
///     Describes an entity, DTO, error type, or enum in the manifest.
/// </summary>
internal sealed record ManifestTypeModel
{
    public required string Type { get; init; }
    public required string SimpleName { get; init; }
    public required ManifestTypeKind Kind { get; init; }
    public string? Boundary { get; init; }
    public string? IdType { get; init; }
    public ManifestTraitsModel? Traits { get; init; }
    public EquatableArray<ManifestPropertyModel> Properties { get; init; } = EquatableArray<ManifestPropertyModel>.Empty;
    public EquatableArray<string> EnumValues { get; init; } = EquatableArray<string>.Empty;
    public string? ErrorCode { get; init; }
    public int? ErrorStatusCode { get; init; }
    public EquatableArray<ManifestErrorExtensionModel> ErrorExtensions { get; init; } = EquatableArray<ManifestErrorExtensionModel>.Empty;

    /// <summary>
    ///     The members this type declares that the serializer strips from every response — tenancy,
    ///     ownership, the concurrency token, the persistence key — and that are therefore not published
    ///     (PRAG0538). Not written into the manifest: it is what the build says about it.
    /// </summary>
    public EquatableArray<string> StrippedMembers { get; init; } = EquatableArray<string>.Empty;
}

internal enum ManifestTypeKind
{
    Entity,
    Dto,
    Error,
    Enum
}

internal sealed record ManifestTraitsModel
{
    public bool IsAuditable { get; init; }
    public bool IsSoftDelete { get; init; }
    public bool IsOwnedEntity { get; init; }
    public bool IsScopedEntity { get; init; }
    public bool IsTenantEntity { get; init; }
    public bool IsConcurrencyAware { get; init; }
}
