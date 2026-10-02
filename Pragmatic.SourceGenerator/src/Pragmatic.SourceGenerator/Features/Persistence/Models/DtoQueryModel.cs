using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating per-DTO query extension methods on IQueryable&lt;TEntity&gt;.
///     Each DTO with [GenerateProjection] gets GetAs{Dto}Async, ListAs{Dto}Async, and As{Dto}.
/// </summary>
internal sealed record DtoQueryModel
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
    ///     The entity Id type (e.g. "System.Guid", "int").
    /// </summary>
    public required string IdType { get; init; }

    /// <summary>
    ///     DTOs with [GenerateProjection] mapped from this entity.
    /// </summary>
    public EquatableArray<DtoQueryEntry> Dtos { get; init; } = EquatableArray<DtoQueryEntry>.Empty;

    /// <summary>
    ///     Whether this model is valid for generation.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(EntityTypeName) &&
                           !string.IsNullOrEmpty(EntityFullTypeName) &&
                           Dtos.Length > 0;
}

/// <summary>
///     A single DTO with projection for query extension generation.
/// </summary>
internal sealed record DtoQueryEntry
{
    /// <summary>
    ///     The DTO type name (e.g. "PropertyDetailDto").
    /// </summary>
    public required string DtoTypeName { get; init; }

    /// <summary>
    ///     The fully qualified DTO type name (e.g. "TestApp.PropertyDetailDto").
    /// </summary>
    public required string DtoFullTypeName { get; init; }
}
