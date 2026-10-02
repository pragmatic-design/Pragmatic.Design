using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms the <c>[GenerateHierarchy]</c> attributes on an entity into a <see cref="HierarchyModel" />,
///     one tree per attribute, each resolved to a self-referencing relation declared on the same entity.
/// </summary>
/// <remarks>
///     <para>
///         The attribute leans on <c>[Relation.ManyToOne&lt;TSelf&gt;]</c> and reads the parent key from the
///         relation's declaration through <c>RelationForeignKeyNaming</c> — the same predictor every other
///         feature uses. Looking for a member called <c>ParentId</c> with <c>GetMembers()</c> would find
///         only a hand-written property: the generated one lives in a file this transform cannot see, so
///         the framework would require exactly what the relation rules forbid.
///     </para>
///     <para>
///         With several self-referencing relations the tree is one of them and <c>Via</c> has to say
///         which; a relation that is required cannot be a tree, because a root has no parent.
///     </para>
/// </remarks>
internal static class HierarchyQueryTransform
{
    public const string GenerateHierarchyAttributeName = "Pragmatic.Persistence.Entity.GenerateHierarchyAttribute";

    public static HierarchyModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var selfRelations = RelationDetection.GetRelations(typeSymbol)
            .Select(pair => pair.Item2)
            .Where(info => info.RelationType == "ManyToOne"
                           && SymbolEqualityComparer.Default.Equals(info.TargetType, typeSymbol))
            .ToList();

        var trees = ImmutableArray.CreateBuilder<HierarchyTreeModel>();
        var problems = ImmutableArray.CreateBuilder<HierarchyProblemModel>();

        foreach (var attribute in context.Attributes)
        {
            var via = attribute.NamedArguments
                .FirstOrDefault(argument => argument.Key == "Via").Value.Value as string;

            var edge = ResolveEdge(selfRelations, via, typeSymbol.Name, problems);
            if (edge is null)
                continue;

            var navigation = RelationDetection.GetNavigationName(edge);
            if (edge.IsRequired)
            {
                problems.Add(new HierarchyProblemModel(HierarchyProblemKind.EdgeRequired, navigation));
                continue;
            }

            trees.Add(new HierarchyTreeModel(
                navigation,
                RelationForeignKeyNaming.Name(edge.ForeignKeyProperty, edge.NavigationName, typeSymbol.Name)));
        }

        return new HierarchyModel
        {
            Namespace = ns,
            TypeName = typeSymbol.Name,
            FullTypeName = typeSymbol.ToDisplayString(),
            IdType = IdTypeOf(typeSymbol),
            Trees = trees.ToImmutable(),
            Problems = problems.ToImmutable(),
            LocationInfo = LocationInfo.From(typeSymbol.Locations.FirstOrDefault())
        };
    }

    /// <summary>The self-referencing relation this attribute means, or <c>null</c> with the reason recorded.</summary>
    private static RelationInfo? ResolveEdge(
        List<RelationInfo> selfRelations,
        string? via,
        string typeName,
        ImmutableArray<HierarchyProblemModel>.Builder problems)
    {
        if (selfRelations.Count == 0)
        {
            problems.Add(new HierarchyProblemModel(HierarchyProblemKind.NoSelfRelation, typeName));
            return null;
        }

        if (!string.IsNullOrEmpty(via))
        {
            var named = selfRelations.FirstOrDefault(
                info => RelationDetection.GetNavigationName(info) == via);
            if (named is null)
                problems.Add(new HierarchyProblemModel(
                    HierarchyProblemKind.ViaNotFound,
                    via!,
                    string.Join(", ", selfRelations.Select(RelationDetection.GetNavigationName))));
            return named;
        }

        if (selfRelations.Count == 1)
            return selfRelations[0];

        problems.Add(new HierarchyProblemModel(
            HierarchyProblemKind.ViaRequired,
            string.Join(", ", selfRelations.Select(RelationDetection.GetNavigationName))));
        return null;
    }

    /// <summary>
    ///     The key's type, read from a hand-written <c>PersistenceId</c>/<c>Id</c> when there is one.
    ///     The generated one is not visible here and is a <c>Guid</c>.
    /// </summary>
    private static string IdTypeOf(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
            if (member is IPropertySymbol { Name: "PersistenceId" or "Id" } prop)
                return prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return "global::System.Guid";
    }
}
