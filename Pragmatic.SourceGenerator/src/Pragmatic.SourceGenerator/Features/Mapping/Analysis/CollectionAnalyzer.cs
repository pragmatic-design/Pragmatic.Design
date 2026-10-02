using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper methods for collection type analysis.
/// </summary>
internal static class CollectionAnalyzer
{
    /// <summary>
    ///     Analyzes a type to determine if it's a collection and its properties.
    /// </summary>
    public static CollectionInfo AnalyzeCollectionType(ITypeSymbol type)
    {
        // Check for Dictionary
        if (type is INamedTypeSymbol { IsGenericType: true } dictType)
        {
            var typeName = dictType.OriginalDefinition.ToDisplayString();
            if (typeName.Contains("Dictionary") || typeName.Contains("IDictionary"))
            {
                var keyType = dictType.TypeArguments[0].ToDisplayString();
                var valueTypeSymbol = dictType.TypeArguments[1];
                var valueType = valueTypeSymbol.ToDisplayString();
                var isValueSimple = TypeAnalyzer.IsSimpleType(valueTypeSymbol);

                // Check if value type is a DTO (has [MapFrom] attribute)
                string? valueDtoType = null;
                if (!isValueSimple)
                {
                    var unwrappedValueType = TypeAnalyzer.UnwrapNullable(valueTypeSymbol);
                    if (unwrappedValueType is INamedTypeSymbol namedValueType)
                    {
                        var hasMapFrom = namedValueType.GetAttributes()
                            .Any(a => a.AttributeClass?.OriginalDefinition.ToDisplayString()
                                .StartsWith("Pragmatic.Mapping.Attributes.MapFromAttribute") == true);
                        if (hasMapFrom)
                            valueDtoType = namedValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                }

                return new CollectionInfo
                {
                    IsDictionary = true,
                    KeyType = keyType,
                    ValueType = valueType,
                    ValueTypeSymbol = valueTypeSymbol,
                    IsValueSimple = isValueSimple,
                    ValueDtoType = valueDtoType
                };
            }
        }

        // Check for Array
        if (type is IArrayTypeSymbol arrayType)
            return new CollectionInfo
            {
                Kind = CollectionKind.Array,
                ElementType = arrayType.ElementType.ToDisplayString(),
                ElementTypeSymbol = arrayType.ElementType,
                IsElementSimple = TypeAnalyzer.IsSimpleType(arrayType.ElementType)
            };

        // Check for other collection types
        if (type is INamedTypeSymbol { IsGenericType: true } genericType)
        {
            var originalDef = genericType.OriginalDefinition.ToDisplayString();
            var elementTypeSymbol = genericType.TypeArguments[0];
            var elementType = elementTypeSymbol.ToDisplayString();

            var kind = originalDef switch
            {
                "System.Collections.Generic.List<T>" => CollectionKind.List,
                "System.Collections.Generic.IList<T>" => CollectionKind.IList,
                "System.Collections.Generic.ICollection<T>" => CollectionKind.ICollection,
                "System.Collections.Generic.IEnumerable<T>" => CollectionKind.IEnumerable,
                "System.Collections.Generic.IReadOnlyList<T>" => CollectionKind.IReadOnlyList,
                "System.Collections.Generic.IReadOnlyCollection<T>" => CollectionKind.IReadOnlyCollection,
                "System.Collections.Generic.HashSet<T>" => CollectionKind.HashSet,
                "System.Collections.Immutable.ImmutableArray<T>" => CollectionKind.ImmutableArray,
                "System.Collections.Immutable.ImmutableList<T>" => CollectionKind.ImmutableList,
                _ => CollectionKind.None
            };

            if (kind != CollectionKind.None)
                return new CollectionInfo
                {
                    Kind = kind,
                    ElementType = elementType,
                    ElementTypeSymbol = elementTypeSymbol,
                    IsElementSimple = TypeAnalyzer.IsSimpleType(elementTypeSymbol)
                };
        }

        return new CollectionInfo();
    }
}

/// <summary>
///     Information about a collection type.
/// </summary>
internal sealed record CollectionInfo
{
    public CollectionKind Kind { get; init; }
    public string? ElementType { get; init; }
    public ITypeSymbol? ElementTypeSymbol { get; init; }
    public bool IsElementSimple { get; init; }
    public bool IsDictionary { get; init; }
    public string? KeyType { get; init; }
    public string? ValueType { get; init; }
    public ITypeSymbol? ValueTypeSymbol { get; init; }
    public bool IsValueSimple { get; init; }
    public string? ValueDtoType { get; init; }
}
