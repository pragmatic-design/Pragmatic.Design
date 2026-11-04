using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The queries derived from specifications (<c>[Query]</c> on a static member returning <c>Specification&lt;T&gt;</c>, #511), and the
///     specification properties a query composes. The derived type is Pragmatic's output (<c>DerivedQueryTemplate</c>), which an engine binds
///     as a shape source: read only where it is declared, so in the compilation with the shape. Its filter is the rule it holds; the rule's
///     parameters are its inputs, as EF Core's half of it reads them.
/// </summary>
internal sealed partial class PragmaticQuery
{
    private const string SpecificationType = "Pragmatic.Specification.Specification`1";

    /// <summary>True for a query derived from a specification: its properties are the rule's inputs, not filters.</summary>
    private bool Derived { get; set; }

    private readonly List<QueryRuleModel> _rules = [];

    /// <summary>The derived queries of the specifications declared in <paramref name="compilation" />, whose type it declares.</summary>
    private static IEnumerable<PragmaticQuery> ReadDerived(Compilation compilation, PragmaticEngine engine, PragmaticEngines engines)
    {
        if (compilation.GetTypeByMetadataName(SpecificationType) is not { } specification)
            yield break;
        foreach (var container in PragmaticNames.AllTypes(compilation.GlobalNamespace).Where(t => t.DeclaringSyntaxReferences.Length > 0 && !t.IsGenericType))
        {
            foreach (var member in container.GetMembers().Where(m => m.IsStatic && m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol))
            {
                var attribute = member.GetAttributes().FirstOrDefault(a => a.AttributeClass is { IsGenericType: true, Name: "QueryAttribute" } c
                    && c.ContainingNamespace.ToDisplayString() + "." == Attributes);
                var returned = member is IMethodSymbol method ? method.ReturnType : ((IPropertySymbol)member).Type;
                if (attribute?.AttributeClass is not { } attributeClass
                    || returned is not INamedTypeSymbol { IsGenericType: true } named || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, specification)
                    || named.TypeArguments[0] is not INamedTypeSymbol entity || !PragmaticShape.IsPragmaticEntity(entity) || !engines.Serves(entity, engine))
                    continue;
                // The name DerivedQueryTemplate writes (SpecificationQueryTransform): {Member}Query, in the container's namespace.
                var name = NamingHelper.AppendSuffix(member.Name, "Query");
                var metadataName = container.ContainingNamespace.IsGlobalNamespace ? name : $"{container.ContainingNamespace.ToDisplayString()}.{name}";
                if (compilation.GetTypeByMetadataName(metadataName) is not { } type)
                    continue; // not bound here: the compilation without the shape
                var result = attributeClass.TypeArguments.Length > 1 ? attributeClass.TypeArguments[1] as INamedTypeSymbol ?? entity : entity;
                var query = new PragmaticQuery(type, entity, result) { Derived = true };
                query.ReadMembers(attribute);
                yield return query;
            }
        }
    }

    /// <summary>
    ///     A specification property: ANDed into the filters as it is, as EF Core's <c>ToSpecification()</c> composes it (a null one is no
    ///     filter). Only a <c>Specification&lt;T&gt;</c> of the query's entity composes with <c>&amp;</c>; any other is refused with its reason.
    /// </summary>
    private void ReadRule(IPropertySymbol property, INamedTypeSymbol specification)
    {
        if (specification.Name != "Specification" || specification.TypeArguments.Length != 1
            || specification.ContainingNamespace.ToDisplayString() != "Pragmatic.Specification"
            || !SymbolEqualityComparer.Default.Equals(specification.TypeArguments[0], Entity))
        {
            Unsupported ??= $"the specification property {property.Name} is not a Specification<{Entity.Name}>, which is the one composed";
            return;
        }

        var access = $"this.{property.Name}";
        _rules.Add(property.Type.NullableAnnotation == NullableAnnotation.Annotated
            ? new QueryRuleModel($"{access} is not null", access + "!")
            : new QueryRuleModel(null, access));
    }
}
