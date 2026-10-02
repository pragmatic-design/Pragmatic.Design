using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper methods for nested DTO analysis.
/// </summary>
internal static class NestedDtoAnalyzer
{
    /// <summary>
    ///     Analyzes a type to determine if it's a nested DTO that requires mapping (for MapFrom direction).
    /// </summary>
    public static NestedInfo AnalyzeNestedDto(ITypeSymbol type, CollectionInfo collectionInfo)
    {
        return AnalyzeNestedDto(type, collectionInfo, MappingDirection.FromEntity);
    }

    /// <summary>
    ///     Analyzes a type to determine if it's a nested DTO that requires mapping.
    /// </summary>
    /// <param name="type">The property type to analyze.</param>
    /// <param name="collectionInfo">Collection analysis info for the type.</param>
    /// <param name="direction">The mapping direction to check for (MapFrom or MapTo).</param>
    public static NestedInfo AnalyzeNestedDto(ITypeSymbol type, CollectionInfo collectionInfo,
        MappingDirection direction)
    {
        var attributePrefix = direction == MappingDirection.FromEntity
            ? "Pragmatic.Mapping.Attributes.MapFromAttribute"
            : "Pragmatic.Mapping.Attributes.MapToAttribute";

        // For collections, check element type
        if (collectionInfo.Kind != CollectionKind.None && !string.IsNullOrEmpty(collectionInfo.ElementType))
        {
            // Simple types (string, int, etc.) don't need DTO mapping
            if (collectionInfo.IsElementSimple)
                return new NestedInfo();

            // Check if element type has the appropriate mapping attribute
            // We need to unwrap nullable to check the actual type
            var elementSymbol = TypeAnalyzer.UnwrapNullable(collectionInfo.ElementTypeSymbol);
            if (elementSymbol is INamedTypeSymbol elementNamedType)
            {
                var hasMappingAttr = elementNamedType.GetAttributes()
                    .Any(a => a.AttributeClass?.OriginalDefinition.ToDisplayString()
                        .StartsWith(attributePrefix) == true);

                // ⚠️ A mutation is a [MapTo] also when it is a child. Without this, the generated body
                // would assign a List<WriteOrderLineMutation> to an ICollection<OrderLine>: CS0266,
                // because the collection would look like a value instead of a set of children.
                // It is the root's deduction, applied one level down.
                if (hasMappingAttr || MutationAnalyzer.IsMutationOver(elementNamedType, direction))
                    // Use FullyQualifiedFormat to get "global::Namespace.TypeName"
                    // This avoids namespace resolution issues in generated code
                    return new NestedInfo
                    {
                        IsElementDto = true,
                        ElementDtoType = elementNamedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    };
            }

            // Not a simple type but no mapping attribute - not a DTO collection
            return new NestedInfo();
        }

        // For non-collections, check if type has the appropriate mapping attribute
        // We need to unwrap nullable to check the actual type
        var unwrappedType = TypeAnalyzer.UnwrapNullable(type);
        if (unwrappedType is INamedTypeSymbol namedType && !TypeAnalyzer.IsSimpleType(unwrappedType))
        {
            var hasMappingAttr = namedType.GetAttributes()
                .Any(a => a.AttributeClass?.OriginalDefinition.ToDisplayString()
                    .StartsWith(attributePrefix) == true);

            // Here too: a mutation is a [MapTo] classified by another attribute.
            if (hasMappingAttr || MutationAnalyzer.IsMutationOver(namedType, direction))
                // Use FullyQualifiedFormat to get "global::Namespace.TypeName"
                // This avoids namespace resolution issues in generated code
                return new NestedInfo
                {
                    IsNested = true,
                    DtoType = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                };
        }

        return new NestedInfo();
    }

    /// <summary>
    ///     PRAG0315: returns the nested DTO's declared <c>[MapFrom&lt;T&gt;]</c> source type name when it
    ///     is UNRELATED to the actual source navigation type (neither the same type nor a base of it) —
    ///     the generated <c>NestedDto.FromEntity(entity.Nav)</c> call would not compile. Null when
    ///     compatible or not determinable.
    /// </summary>
    public static string? GetMapFromSourceMismatch(ITypeSymbol dtoPropertyType, ITypeSymbol sourceNavigationType)
    {
        if (TypeAnalyzer.UnwrapNullable(dtoPropertyType) is not INamedTypeSymbol nestedDto)
            return null;

        var mapFromAttr = nestedDto.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Mapping.Attributes.MapFromAttribute") == true);

        if (mapFromAttr?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType
            || attrType.TypeArguments[0] is not INamedTypeSymbol declaredSource)
            return null;

        // Compatible when the navigation type IS the declared source or derives from it.
        for (var t = TypeAnalyzer.UnwrapNullable(sourceNavigationType); t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, declaredSource))
                return null;

        return declaredSource.ToDisplayString();
    }
}

/// <summary>
///     The direction of mapping.
/// </summary>
internal enum MappingDirection
{
    /// <summary>Entity → DTO (FromEntity)</summary>
    FromEntity,

    /// <summary>DTO → Entity (ToEntity)</summary>
    ToEntity
}

/// <summary>
///     Information about a nested DTO property.
/// </summary>
internal sealed record NestedInfo
{
    public bool IsNested { get; init; }
    public string? DtoType { get; init; }
    public bool IsElementDto { get; init; }
    public string? ElementDtoType { get; init; }
}
