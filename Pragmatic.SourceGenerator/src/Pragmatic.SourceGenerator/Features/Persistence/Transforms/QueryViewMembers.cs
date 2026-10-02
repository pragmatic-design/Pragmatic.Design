using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The members a <c>[QueryView]</c> can name on an entity: its group keys, the navigations of a
///     <c>Via</c>, and what an aggregate reads.
/// </summary>
internal static class QueryViewMembers
{
    /// <summary>
    ///     A property the entity will have: declared on it or a base type, or one a generator adds — a
    ///     relation's foreign key or navigation, a trait's column.
    /// </summary>
    /// <remarks>
    ///     The generated ones are not in this compilation while the transform runs: asking the symbol
    ///     alone dropped every key and path a relation writes, in silence.
    ///     <c>Declared</c> is null for a generated member, and <c>Type</c> is null for a generated
    ///     scalar, which only <c>TypeName</c> describes.
    /// </remarks>
    public static (IPropertySymbol? Declared, ITypeSymbol? Type, string TypeName)? Find(ITypeSymbol type, string name)
    {
        if (FindPublicProperty(type, name) is { } declared)
            return (declared, declared.Type, declared.Type.ToDisplayString());

        if (type is not INamedTypeSymbol entity)
            return null;

        foreach (var generated in TraitPropertyResolver.GetGeneratedProperties(entity))
        {
            if (generated.Name == name)
                return (null, generated.TypeSymbol, generated.TypeSymbol?.ToDisplayString() ?? generated.TypeFullName);
        }

        return null;
    }

    /// <summary>
    ///     Finds a public instance property by name on the type or any of its base types.
    /// </summary>
    private static IPropertySymbol? FindPublicProperty(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var match = current.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.Name == name && p.DeclaredAccessibility == Accessibility.Public);

            if (match is not null)
                return match;
        }

        return null;
    }
}
