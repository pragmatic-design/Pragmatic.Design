using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating OnModelCreating inheritance mapping configuration.
/// </summary>
internal sealed record InheritanceMappingModel
{
    public required string Namespace { get; init; }
    public required string BaseTypeName { get; init; }
    public required string BaseFullTypeName { get; init; }
    public required string Strategy { get; init; }
    public string DiscriminatorColumn { get; init; } = "Discriminator";

    /// <summary>Concrete derived types in the hierarchy.</summary>
    public EquatableArray<DerivedTypeModel> DerivedTypes { get; init; } = EquatableArray<DerivedTypeModel>.Empty;

    public bool IsValid => !string.IsNullOrEmpty(BaseTypeName) && !string.IsNullOrEmpty(Strategy);
    public bool HasDerivedTypes => !DerivedTypes.IsDefaultOrEmpty;
}

internal sealed record DerivedTypeModel
{
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public string? DiscriminatorValue { get; init; }
}
