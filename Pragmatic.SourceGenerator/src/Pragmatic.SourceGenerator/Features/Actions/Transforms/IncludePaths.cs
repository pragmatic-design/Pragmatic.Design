using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Checks an <c>Include</c> path — of a <c>[LoadEntity]</c>, or an <c>[EagerLoad]</c> of a mutation or a query —
///     against the entity: every segment a navigation of the type the previous one leads to.
/// </summary>
/// <remarks>
///     A navigation is declared or generated. The generated ones — the two sides of a <c>[Relation]</c> — are
///     not on the symbol while this generator runs, so they are asked of <see cref="TraitPropertyResolver" />,
///     the same prediction Mapping and the mutation's child analysis use: asking the symbol alone reported
///     every relation navigation as unknown.
/// </remarks>
internal static class IncludePaths
{
    private const string EagerLoadAttribute = "Pragmatic.Actions.Mutation.EagerLoadAttribute";

    /// <summary>
    ///     The <c>[EagerLoad]</c> paths of a mutation or a query, checked against its entity: those that name
    ///     navigations, in declaration order and once each, and those that do not, with the first segment that fails.
    /// </summary>
    /// <remarks>
    ///     One reader for both, as the <c>[LoadEntity(Include)]</c> check is: the path was copied into the
    ///     generated <c>Include</c> unread, and a typo or a scalar was an EF Core exception at the first request.
    /// </remarks>
    public static (ImmutableArray<string> Valid, ImmutableArray<(string Path, string Segment)> Invalid) EagerLoads(
        INamedTypeSymbol operation, ITypeSymbol entity)
    {
        var valid = ImmutableArray.CreateBuilder<string>();
        var invalid = ImmutableArray.CreateBuilder<(string, string)>();
        foreach (var attribute in operation.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != EagerLoadAttribute
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not string path
                || string.IsNullOrWhiteSpace(path)
                || valid.Contains(path))
                continue;

            if (FirstSegmentThatIsNotANavigation(entity, path) is { } segment)
                invalid.Add((path, segment));
            else
                valid.Add(path);
        }

        return (valid.ToImmutable(), invalid.ToImmutable());
    }

    /// <summary>The first segment of <paramref name="path" /> that is not a navigation, or null when all are.</summary>
    public static string? FirstSegmentThatIsNotANavigation(ITypeSymbol entity, string path)
    {
        var current = entity as INamedTypeSymbol;
        foreach (var segment in path.Split('.').Select(s => s.Trim()))
        {
            var next = current is null ? null : NavigationTarget(current, segment);
            if (next is null)
                return segment;
            current = next;
        }

        return null;
    }

    /// <summary>The type a navigation named <paramref name="name" /> leads to — the element of a collection.</summary>
    private static INamedTypeSymbol? NavigationTarget(INamedTypeSymbol type, string name)
    {
        for (var declaring = type; declaring is not null; declaring = declaring.BaseType)
        {
            var property = declaring.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic);
            if (property is not null)
                return Navigable(ElementOf(property.Type) ?? property.Type);
        }

        // A generated collection navigation carries its element; a generated scalar (a trait's CreatedAt, a
        // foreign key) carries no symbol and is no navigation.
        var generated = TraitPropertyResolver.GetGeneratedProperties(type)
            .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        return generated.TypeSymbol is { } symbol ? Navigable(ElementOf(symbol) ?? symbol) : null;
    }

    /// <summary>A class other than <c>string</c>: what EF Core can include.</summary>
    private static INamedTypeSymbol? Navigable(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: not SpecialType.System_String } named
            ? named
            : null;

    private static ITypeSymbol? ElementOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            || type.SpecialType == SpecialType.System_String)
            return null;

        var isEnumerable = named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                           || named.AllInterfaces.Any(i =>
                               i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        return isEnumerable ? named.TypeArguments[0] : null;
    }
}
