using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     How a single reference navigation is written back to the entity.
/// </summary>
/// <remarks>
///     The twin of <see cref="CollectionWriteAnalyzer" /> for the child that is one rather than many.
///     There is far less to work out here — a reference has no key and nothing to match — so this
///     answers one question: what did the author declare, if anything.
/// </remarks>
internal static class ReferenceWriteAnalyzer
{
    private const string MappingAttributes = "Pragmatic.Mapping.Attributes";

    /// <summary>The default, and what every reference did before the attribute existed.</summary>
    internal const string Merge = "Merge";

    /// <summary>
    ///     <c>[ReferenceStrategy(ReferenceStrategy.X)]</c> on the property, or <see cref="Merge" />.
    /// </summary>
    /// <remarks>
    ///     The constructor argument arrives as the enum's underlying <c>int</c>: the attribute is read
    ///     from a symbol, so the enum member's name is not available without resolving the type. The
    ///     mapping is positional and must follow <c>ReferenceStrategy</c>'s declaration order — the
    ///     same contract <see cref="CollectionWriteAnalyzer" /> already lives with.
    /// </remarks>
    internal static string ReadStrategy(IPropertySymbol dtoProperty)
    {
        foreach (var attribute in dtoProperty.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "ReferenceStrategyAttribute" } declaration
                || declaration.ContainingNamespace?.ToDisplayString() != MappingAttributes
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not int value)
                continue;

            return value switch
            {
                0 => Merge,
                1 => "Detach",
                2 => "Replace",
                3 => "Ignore",
                _ => Merge,
            };
        }

        return Merge;
    }
}
