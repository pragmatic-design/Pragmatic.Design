using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating per-DTO include extension methods on IQueryable&lt;TEntity&gt;.
///     Groups all DTOs that map from the same entity, with their needed navigation includes.
/// </summary>
internal sealed record DtoIncludeModel
{
    /// <summary>
    ///     Entity namespace (for the generated extension class).
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     The entity type name.
    /// </summary>
    public required string EntityTypeName { get; init; }

    /// <summary>
    ///     The fully qualified entity type name.
    /// </summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>
    ///     All same-boundary navigations available on the entity.
    /// </summary>
    public EquatableArray<string> AllNavigationNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     DTOs mapped from this entity, each with their required navigation includes.
    /// </summary>
    public EquatableArray<DtoIncludeEntry> Dtos { get; init; } = EquatableArray<DtoIncludeEntry>.Empty;

    /// <summary>
    ///     Whether this model is valid for generation.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(EntityTypeName) &&
                           !string.IsNullOrEmpty(EntityFullTypeName) &&
                           AllNavigationNames.Length > 0;
}

/// <summary>
///     A single DTO with its required navigation include paths.
/// </summary>
internal sealed record DtoIncludeEntry
{
    /// <summary>
    ///     The DTO type name (e.g. "PropertyDetailDto").
    /// </summary>
    public required string DtoTypeName { get; init; }

    /// <summary>
    ///     The DTO's fully qualified name, used to name its <c>RequiredNavigations</c>.
    /// </summary>
    /// <remarks>
    ///     The navigations themselves are not carried here. Matching DTO property names against entity
    ///     navigation names finds nothing for a DTO that flattens:
    ///     <c>RoomTypeSummaryDto</c> has <c>PropertyName</c>, the navigation is <c>Property</c>. Mapping
    ///     works the same list out properly — explicit paths, flattening, nested DTOs, collections — and
    ///     publishes it on the DTO, so the generated method reads that instead of a second, poorer copy.
    /// </remarks>
    public required string DtoFullTypeName { get; init; }
}

/// <summary>
///     Raw DTO info extracted from [MapFrom&lt;TEntity&gt;] before matching with entity navigations.
/// </summary>
internal sealed record DtoMetadataInfo
{
    /// <summary>
    ///     The DTO type name.
    /// </summary>
    public required string DtoTypeName { get; init; }

    /// <summary>
    ///     The fully qualified DTO type name (e.g. "TestApp.PropertyDetailDto").
    /// </summary>
    public required string DtoFullTypeName { get; init; }

    /// <summary>
    ///     The entity type's fully qualified name (from MapFrom generic argument).
    /// </summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>
    ///     Property names on the DTO (for matching to entity navigations).
    /// </summary>
    public EquatableArray<string> PropertyNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether this DTO has [GenerateProjection].
    /// </summary>
    public bool HasProjection { get; init; }
}
