using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Group membership: <c>[EndpointGroup&lt;TGroup&gt;]</c> on an endpoint, and on a group that hangs
///     from a parent.
/// </summary>
internal static partial class EndpointTransform
{
    /// <summary>
    ///     The group a type declares it belongs to, via <c>[EndpointGroup&lt;TGroup&gt;]</c> — as the
    ///     symbol the attribute carries, which may be an error symbol when the name resolved to nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Matched by simple name and arity, never by <c>ToDisplayString()</c>. A comparison
    ///         against a non-generic display string never matches the generic form, so
    ///         <c>[Endpoint&lt;TGroup&gt;]</c> would compile and generate nothing, and no diagnostic
    ///         would say so.
    ///     </para>
    ///     <para>
    ///         ⚠️ And the symbol is used, not its name. Re-finding the group by walking the endpoint's
    ///         own assembly for the display string fails in two ways with the same silence: a group that
    ///         does not exist produces no PRAG0507 — the one case the descriptor is named after — and a
    ///         group declared in a referenced assembly is "not found" too, so its prefix is dropped and
    ///         the endpoint answers at the bare route.
    ///     </para>
    /// </remarks>
    internal static INamedTypeSymbol? GroupOf(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "EndpointGroupAttribute" } attributeClass)
                continue;

            if (attributeClass.TypeArguments.Length == 1
                && attributeClass.TypeArguments[0] is INamedTypeSymbol group)
                return group;
        }

        return null;
    }

    internal static EndpointGroupModel? ParseGroup(INamedTypeSymbol? group)
        => ParseGroup(group, visited: null);

    private static EndpointGroupModel? ParseGroup(INamedTypeSymbol? group, HashSet<INamedTypeSymbol>? visited)
    {
        if (group is null)
            return null;

        var typeName = group.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // A name the compiler could not bind. The compiler says so too, but only as "type not found";
        // this one says what was lost with it.
        if (group.TypeKind == TypeKind.Error)
            return new EndpointGroupModel { TypeName = typeName, NotFound = true };

        // Guard against circular parent references to prevent StackOverflow.
        visited ??= new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        if (!visited.Add(group))
            return new EndpointGroupModel { TypeName = typeName };

        var groupAttr = group.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.EndpointGroup);

        if (groupAttr is null)
            return new EndpointGroupModel { TypeName = typeName, TypeExistsButNotGroup = true };

        string? routePrefix = null;
        string? tag = null;
        string? version = null;

        if (groupAttr.ConstructorArguments.Length > 0)
            routePrefix = groupAttr.ConstructorArguments[0].Value?.ToString();

        foreach (var namedArg in groupAttr.NamedArguments)
            switch (namedArg.Key)
            {
                case "Tag":
                    tag = namedArg.Value.Value?.ToString();
                    break;
                case "Version":
                    version = namedArg.Value.Value?.ToString();
                    break;
            }

        // The parent is a membership like any other, declared the same way. It was `Parent = typeof(X)`,
        // a third spelling for the same idea, used by one test.
        var parent = ParseGroup(GroupOf(group), visited);

        return new EndpointGroupModel
        {
            TypeName = typeName,
            RoutePrefix = routePrefix,
            Tag = tag,
            Version = version,
            Parent = parent
        };
    }
}
