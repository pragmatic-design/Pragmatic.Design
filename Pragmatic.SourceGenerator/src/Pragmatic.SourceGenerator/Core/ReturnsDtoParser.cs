using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Reads <c>[ReturnsDto&lt;T&gt;]</c> and what the type it names needs in order to be built.
/// </summary>
/// <remarks>
///     Three features ask the same question — Endpoints for the response type, Resource for the
///     scaffolding override, Actions for the navigations a load has to bring with it — and three
///     private copies of "find this attribute and take its type argument" would be three chances to
///     disagree about which attribute counts.
/// </remarks>
internal static class ReturnsDtoParser
{
    private const string AttributeName = "ReturnsDtoAttribute";
    private const string AttributeNamespace = "Pragmatic.Persistence.Entity";
    private const string MappingNamespace = "Pragmatic.Mapping.Attributes";

    /// <summary>
    ///     The DTO an operation declares it answers with, or null when it declares none.
    /// </summary>
    /// <remarks>
    ///     Matched on name plus namespace, never on <c>ToDisplayString()</c>: a generic attribute
    ///     displays with its type argument, so the comparison would never hold and the branch would
    ///     never run.
    /// </remarks>
    public static INamedTypeSymbol? Read(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: AttributeName } declaration
                && declaration.ContainingNamespace?.ToDisplayString() == AttributeNamespace
                && declaration.TypeArguments.Length == 1
                && declaration.TypeArguments[0] is INamedTypeSymbol dto)
                return dto;
        }

        return null;
    }

    /// <summary>
    ///     Whether the type carries <c>[MapFrom&lt;T&gt;]</c>, and therefore has a generated
    ///     <c>RequiredNavigations</c> a caller can name.
    /// </summary>
    /// <remarks>
    ///     The navigations themselves are <b>not</b> computed here. Mapping already works them out —
    ///     explicit paths, auto-flattening, nested DTOs, collections of DTOs — and publishes them as
    ///     <c>TDto.RequiredNavigations</c>. Re-deriving them would be a second, poorer version of a
    ///     rule that exists: the one here would have seen <c>[MapProperty("Property.Name")]</c> and
    ///     missed a nested DTO entirely.
    /// </remarks>
    public static bool MapsFromAnEntity(INamedTypeSymbol dto)
    {
        foreach (var attribute in dto.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "MapFromAttribute", TypeArguments.Length: 1 } mapFrom
                && mapFrom.ContainingNamespace?.ToDisplayString() == MappingNamespace)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether the type carries <c>[GenerateProjection]</c>, which is what produces the
    ///     <c>Projection</c> expression a query's <c>Apply</c> names.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="MapsFromAnEntity" /> because the two attributes do different
    ///     things and only one of them was ever checked: <c>[MapFrom&lt;T&gt;]</c> gives
    ///     <c>FromEntity</c> and <c>Selector</c>, which map an object already in memory.
    /// </remarks>
    public static bool GeneratesAProjection(INamedTypeSymbol dto)
    {
        foreach (var attribute in dto.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "GenerateProjectionAttribute" } generate
                && generate.ContainingNamespace?.ToDisplayString() == MappingNamespace)
                return true;
        }

        return false;
    }
}
