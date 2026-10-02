using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads <c>[SearchAcross]</c> off a property: the columns it searches, and whether case matters.
/// </summary>
/// <remarks>
///     One reader for the grid filter and the query, which both honour the attribute. Two readers of one
///     fact is how <c>IgnoreCase</c> would be read by one and ignored by the other.
/// </remarks>
internal static class SearchAcrossReader
{
    public const string AttributeName = "Pragmatic.Persistence.Query.Attributes.SearchAcrossAttribute";

    /// <summary>The attribute's columns and <c>IgnoreCase</c>, or null when the property does not carry it.</summary>
    public static (ImmutableArray<string> Paths, bool IgnoreCase)? Read(IPropertySymbol property)
    {
        var attribute = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeName);
        if (attribute is null)
            return null;

        var paths = attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0] is { Kind: TypedConstantKind.Array } names
            ? names.Values.Where(v => v.Value is string).Select(v => (string)v.Value!).ToImmutableArray()
            : ImmutableArray<string>.Empty;

        var ignoreCase = attribute.NamedArguments
            .Any(a => a is { Key: "IgnoreCase", Value.Value: true });

        return (paths, ignoreCase);
    }
}
