using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads <c>[QueryStrategy]</c> off a query type. Used by <see cref="PublishedQueryTransform" /> for
///     the read contract and by <c>EndpointTransform</c> for the generated GET handler — the two places
///     that build the queryable a query is executed over.
/// </summary>
internal static class QueryStrategyParser
{
    private const string AttributeName = "QueryStrategyAttribute";
    private const string AttributeNamespace = "Pragmatic.Persistence.Query";

    /// <summary>
    ///     The strategy the type declares, or <c>null</c> when it declares none.
    /// </summary>
    /// <remarks>
    ///     Null rather than a default, so "declared nothing" and "declared the default" stay distinct:
    ///     the callers emit exactly what they emitted before the attribute was read for the first, and
    ///     name the strategy for the second.
    /// </remarks>
    public static QueryStrategyKind? Read(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null
                || attributeClass.Name != AttributeName
                || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
            {
                continue;
            }

            // Strategy is an init-only named argument whose C# default is Entity, so the bare
            // [QueryStrategy] means Entity — the same value the property would report at runtime.
            var declared = attribute.NamedArguments
                .FirstOrDefault(argument => argument.Key == "Strategy").Value.Value;

            return declared is int value and >= (int)QueryStrategyKind.Projection and <= (int)QueryStrategyKind.Raw
                ? (QueryStrategyKind)value
                : QueryStrategyKind.Entity;
        }

        return null;
    }
}
