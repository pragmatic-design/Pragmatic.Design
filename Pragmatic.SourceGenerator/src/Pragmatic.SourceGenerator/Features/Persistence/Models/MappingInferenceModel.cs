namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for mapping inference results.
///     Contains information about includes and projections inferred from DTO mapping.
/// </summary>
internal sealed record MappingInferenceModel
{
    /// <summary>
    ///     The source entity type (fully qualified).
    /// </summary>
    public required string EntityTypeFullName { get; init; }

    /// <summary>
    ///     The target DTO type (fully qualified).
    /// </summary>
    public required string DtoTypeFullName { get; init; }

    /// <summary>
    ///     The DTO type name (simple).
    /// </summary>
    public required string DtoTypeName { get; init; }

    /// <summary>
    ///     The namespace of the DTO.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     Include paths inferred from navigation properties.
    /// </summary>
    public IReadOnlyList<IncludePathModel> IncludePaths { get; init; } = [];

    /// <summary>
    ///     Property mappings for projection.
    /// </summary>
    public IReadOnlyList<PropertyMappingModel> PropertyMappings { get; init; } = [];

    /// <summary>
    ///     Whether there are any includes to generate.
    /// </summary>
    public bool HasIncludes => IncludePaths.Count > 0;

    /// <summary>
    ///     Whether the inference is valid.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(EntityTypeFullName) && !string.IsNullOrEmpty(DtoTypeFullName);
}

/// <summary>
///     Model for an include path (navigation property chain).
/// </summary>
internal sealed record IncludePathModel
{
    /// <summary>
    ///     The navigation property path segments.
    ///     For example: ["Customer"] or ["Lines", "Product"]
    /// </summary>
    public required IReadOnlyList<string> PathSegments { get; init; }

    /// <summary>
    ///     Whether this is a collection navigation.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     The target entity type of the navigation.
    /// </summary>
    public required string TargetEntityType { get; init; }

    /// <summary>
    ///     The depth level (0 = direct include, 1+ = then include).
    /// </summary>
    public int Depth => PathSegments.Count - 1;

    /// <summary>
    ///     Gets the full path as a string (e.g., "Customer" or "Lines.Product").
    /// </summary>
    public string FullPath => string.Join(".", PathSegments);
}

/// <summary>
///     Model for a property mapping in projection.
/// </summary>
internal sealed record PropertyMappingModel
{
    /// <summary>
    ///     The DTO property name.
    /// </summary>
    public required string DtoPropertyName { get; init; }

    /// <summary>
    ///     The entity property path (may include navigation, e.g., "Customer.Name").
    /// </summary>
    public required string EntityPropertyPath { get; init; }

    /// <summary>
    ///     The DTO property type.
    /// </summary>
    public required string DtoPropertyType { get; init; }

    /// <summary>
    ///     Whether this property maps to a nested DTO (requires sub-projection).
    /// </summary>
    public bool IsNestedDto { get; init; }

    /// <summary>
    ///     The nested DTO type if IsNestedDto is true.
    /// </summary>
    public string? NestedDtoType { get; init; }

    /// <summary>
    ///     Whether this is a collection property.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     The collection element type if IsCollection is true.
    /// </summary>
    public string? CollectionElementType { get; init; }
}
