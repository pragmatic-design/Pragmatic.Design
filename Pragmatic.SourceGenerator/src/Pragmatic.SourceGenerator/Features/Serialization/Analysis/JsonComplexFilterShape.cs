using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     The JSON shape of a query's <c>[ComplexFilter]</c> properties.
/// </summary>
/// <remarks>
///     A complex filter arrives as a JSON string in the query string and is deserialized into a real
///     type, so it is as much a wire shape as a request body — and it was covered by nothing. It went
///     unnoticed because the generated handler deserialized it through a private, empty
///     <c>JsonSerializerOptions</c>, which quietly fell back to reflection; the moment that call was
///     given typed metadata, two Showcase query tests failed with "no metadata for
///     PropertyLocationFilter". The fallback had been hiding the gap, not filling it.
/// </remarks>
internal static class JsonComplexFilterShape
{
    private static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    ///     The contribution covering every complex-filter type the query declares, or <c>null</c>.
    /// </summary>
    public static JsonRootContribution? For(INamedTypeSymbol query)
    {
        JsonRootContribution? merged = null;

        foreach (var property in query.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.SetMethod is null)
                continue;

            if (!HasComplexFilterAttribute(property) || property.Type is not INamedTypeSymbol filterType)
                continue;

            if (JsonShapeExtractor.TryExtractClosure(filterType, out var objects, out var leaves, out var collections))
                merged = JsonRootContribution.Merge(merged, new JsonRootContribution(objects, leaves, collections));
        }

        return merged;
    }

    private static bool HasComplexFilterAttribute(IPropertySymbol property)
        => property.GetAttributes().Any(static a =>
            a.AttributeClass?.Name is "ComplexFilterAttribute" or "ComplexFilter");
}
