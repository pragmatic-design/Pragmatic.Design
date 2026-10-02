using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     A member carrying <c>[Endpoint]</c>, and whether anything derives a route from it.
/// </summary>
/// <remarks>
///     The attribute is legal on a member so that a <b>specification</b> can say where its derived query
///     answers: a static member returning <c>Specification&lt;TEntity&gt;</c> that also carries
///     <c>[Query]</c>. Everything else is a route nobody maps, which is what <c>PRAG0525</c> reports.
/// </remarks>
internal readonly record struct DerivedRouteHost(string MemberName, bool DerivesAQuery, LocationInfo? Location)
{
    private const string SpecificationMetadataName = "Pragmatic.Specification.Specification`1";
    private const string QueryAttributePrefix = "Pragmatic.Persistence.Query.Attributes.QueryAttribute<";

    /// <summary>Reads the member the attribute sits on.</summary>
    /// <remarks>
    ///     ⚠️ Total on purpose. Answering <c>null</c> for a symbol that is neither a method nor a
    ///     property would let the caller drop it — so a route nobody maps would go unreported in the
    ///     one case nobody expected. It cannot derive a query either way, which is what
    ///     <c>PRAG0525</c> already says about every other member.
    /// </remarks>
    public static DerivedRouteHost Describe(GeneratorAttributeSyntaxContext context)
    {
        var (returnType, member) = context.TargetSymbol switch
        {
            IMethodSymbol m => (m.ReturnType, (ISymbol)m),
            IPropertySymbol p => (p.Type, p),
            _ => (null, null)
        };

        if (returnType is null || member is null)
            return new DerivedRouteHost(
                context.TargetSymbol.Name,
                DerivesAQuery: false,
                LocationInfo.From(context.TargetSymbol.Locations.FirstOrDefault()));

        return new DerivedRouteHost(
            member.Name,
            DerivesAQuery: member.IsStatic
                           && IsSpecification(context.SemanticModel.Compilation, returnType)
                           && CarriesQuery(member),
            LocationInfo.From(member.Locations.FirstOrDefault()));
    }

    private static bool IsSpecification(Compilation compilation, ITypeSymbol type)
    {
        var specification = compilation.GetTypeByMetadataName(SpecificationMetadataName);

        return specification is not null
               && type is INamedTypeSymbol { IsGenericType: true } named
               && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, specification);
    }

    private static bool CarriesQuery(ISymbol member)
        => member.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString().StartsWith(QueryAttributePrefix, System.StringComparison.Ordinal)
            == true);
}
