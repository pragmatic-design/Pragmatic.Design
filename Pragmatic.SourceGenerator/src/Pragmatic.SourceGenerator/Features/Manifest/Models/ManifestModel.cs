using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Manifest.Models;

/// <summary>
///     Root manifest model aggregating all compile-time API metadata for one assembly.
/// </summary>
internal sealed record ManifestModel
{
    public required string Assembly { get; init; }
    public required string SchemaVersion { get; init; }
    public EquatableArray<ManifestBoundaryModel> Boundaries { get; init; } = EquatableArray<ManifestBoundaryModel>.Empty;
    public EquatableArray<ManifestEndpointModel> Endpoints { get; init; } = EquatableArray<ManifestEndpointModel>.Empty;
    public EquatableArray<ManifestTypeModel> Types { get; init; } = EquatableArray<ManifestTypeModel>.Empty;
    public EquatableArray<ManifestActionModel> Actions { get; init; } = EquatableArray<ManifestActionModel>.Empty;
    public EquatableArray<ManifestPermissionModel> Permissions { get; init; } = EquatableArray<ManifestPermissionModel>.Empty;
    public EquatableArray<ManifestValidationModel> ValidationRules { get; init; } = EquatableArray<ManifestValidationModel>.Empty;

    /// <summary>
    ///     Whether the assembly can name <c>Pragmatic.Endpoints.Manifest.ManifestRegistry</c>, and so
    ///     registers its manifest at load time for the runtime OpenAPI enrichment to read.
    /// </summary>
    public bool RegistersAtLoad { get; init; }
}

internal sealed record ManifestBoundaryModel
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public EquatableArray<string> SubBoundaries { get; init; } = EquatableArray<string>.Empty;
}
