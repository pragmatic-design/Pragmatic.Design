using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper for detecting circular references in DTO mappings.
/// </summary>
internal static class CircularReferenceDetector
{
    /// <summary>
    ///     Detects if there are circular references in the DTO mapping graph.
    /// </summary>
    public static bool DetectCircularReferences(
        INamedTypeSymbol dtoType,
        INamedTypeSymbol sourceType,
        HashSet<string> visited)
    {
        var key = $"{dtoType.ToDisplayString()}:{sourceType.ToDisplayString()}";
        if (!visited.Add(key))
            return true; // Circular detected

        foreach (var prop in PropertyAnalyzer.GetAllProperties(dtoType))
        {
            var propTypeSymbol = prop.Type;

            // For collections, resolve the element type
            propTypeSymbol = ResolveElementType(propTypeSymbol) ?? propTypeSymbol;

            if (propTypeSymbol is not INamedTypeSymbol propType)
                continue;

            // Check if property type has [MapFrom]
            var mapFromAttr = propType.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.OriginalDefinition.ToDisplayString()
                    .StartsWith("Pragmatic.Mapping.Attributes.MapFromAttribute") == true);

            if (mapFromAttr?.AttributeClass is INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            {
                var nestedSourceType = attrType.TypeArguments[0] as INamedTypeSymbol;
                if (nestedSourceType is not null)
                    if (DetectCircularReferences(propType, nestedSourceType, visited))
                        return true;
            }
        }

        visited.Remove(key);
        return false;
    }

    /// <summary>
    ///     Extracts the element type from collection types (arrays, generic collections).
    ///     Returns null if the type is not a collection.
    /// </summary>
    private static ITypeSymbol? ResolveElementType(ITypeSymbol type)
    {
        // Array: T[]
        if (type is IArrayTypeSymbol arrayType)
            return arrayType.ElementType;

        // Generic collections: IEnumerable<T>, ICollection<T>, IList<T>, List<T>, etc.
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } namedType)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();

            // Dictionaries: follow the VALUE type — a Dictionary<K, NestedDto> whose value cycles back
            // must be detected, otherwise FromEntity recurses on the value without a visited set.
            if (originalDef.Contains("Dictionary") || originalDef.Contains("IDictionary"))
                return namedType.TypeArguments.Length == 2 ? namedType.TypeArguments[1] : null;

            // Standard single-element generic collections
            if (originalDef is
                "System.Collections.Generic.List<T>" or
                "System.Collections.Generic.IList<T>" or
                "System.Collections.Generic.ICollection<T>" or
                "System.Collections.Generic.IEnumerable<T>" or
                "System.Collections.Generic.IReadOnlyList<T>" or
                "System.Collections.Generic.IReadOnlyCollection<T>" or
                "System.Collections.Generic.HashSet<T>")
            {
                return namedType.TypeArguments[0];
            }
        }

        return null;
    }
}
