using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     A collection of keys that chooses which rows a navigation points at — <c>[LinkIds]</c>.
/// </summary>
/// <remarks>
///     Separate from <see cref="CollectionWriteAnalyzer" /> because there is nothing to match and
///     nothing to build: the caller sends keys, not shapes, and the rows are linked rather than
///     written. What this works out is which navigation, which key, and from which entity a stub
///     would be built.
/// </remarks>
internal static class LinkIdsAnalyzer
{
    private const string MappingAttributes = "Pragmatic.Mapping.Attributes";

    /// <param name="Navigation">The navigation on the entity.</param>
    /// <param name="Key">The related entity's key property.</param>
    /// <param name="Strategy">One of <c>CollectionStrategy</c>, as a name.</param>
    internal readonly record struct LinkIds(string Navigation, string Key, string Strategy);

    /// <summary>
    ///     <c>[LinkIds("Nav")]</c> on the property, or null when it carries none.
    /// </summary>
    internal static LinkIds? Read(IPropertySymbol dtoProperty)
    {
        foreach (var attribute in dtoProperty.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "LinkIdsAttribute" } declaration
                || declaration.ContainingNamespace?.ToDisplayString() != MappingAttributes
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not string navigation
                || navigation.Length == 0)
                continue;

            var key = "PersistenceId";
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Key" && named.Value.Value is string declaredKey && declaredKey.Length > 0)
                    key = declaredKey;
            }

            // The strategy keeps its usual vocabulary and its usual attribute: a list of ids is still
            // a collection, and Sync/AddOnly/Replace/Ignore mean here what they mean there.
            var strategy = CollectionWriteAnalyzer.ReadDeclaredStrategyOrDefault(dtoProperty, "Sync");

            return new LinkIds(navigation, key, strategy);
        }

        return null;
    }
}
