using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The members below a JSON column whose JSON name is not their own (<c>[JsonPropertyName]</c>), keyed by their path of property names from
///     the column: what the runtime path names in a JSON path. The same rule as the compile-time path (<c>LambdaTranslator.JsonPath</c>), handed to
///     the runtime instead of read there with reflection.
/// </summary>
/// <remarks>
///     A collection is crossed to its element (<c>Chapters.Title</c> is a chapter's title): a predicate inside an <c>Any</c> over the array names
///     the element's members, and the runtime re-roots the table under the array's path. Bounded by depth and by the types already on the path,
///     since a JSON type may contain itself.
/// </remarks>
internal static class JsonNames
{
    private const int MaxDepth = 8;

    public static List<(string Path, string Name)> Renamed(ITypeSymbol root)
    {
        var renamed = new List<(string, string)>();
        Walk(root, "", new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), renamed, 0);
        return renamed;
    }

    private static void Walk(ITypeSymbol type, string prefix, HashSet<ITypeSymbol> onPath, List<(string, string)> renamed, int depth)
    {
        type = Element(Underlying(type));
        if (depth >= MaxDepth || type is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named
            || named.SpecialType != SpecialType.None || named.ContainingNamespace?.ToDisplayString().StartsWith("System", System.StringComparison.Ordinal) == true
            || !onPath.Add(named))
            return;

        foreach (var property in named.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public && !p.IsIndexer))
        {
            var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
            var attribute = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsonPropertyNameAttribute");
            if (attribute is { ConstructorArguments.Length: > 0 } && attribute.ConstructorArguments[0].Value is string name && name != property.Name)
                renamed.Add((path, name));
            Walk(property.Type, path, onPath, renamed, depth + 1);
        }

        onPath.Remove(named);
    }

    private static ITypeSymbol Underlying(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    /// <summary>An array's or an enumerable's element; the type itself otherwise (a string is not a collection here).</summary>
    private static ITypeSymbol Element(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;
        if (type.SpecialType == SpecialType.System_String)
            return type;
        var enumerable = type.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        return enumerable is null ? type : enumerable.TypeArguments[0];
    }
}
