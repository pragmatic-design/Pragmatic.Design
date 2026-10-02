using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     The roles that lean on a relation: <c>[PartOf]</c> names the edge a part is written along, and
///     that edge has to exist, be unambiguous, and — for a part with no life of its own — cascade.
/// </summary>
/// <remarks>
///     <para>
///         Structure and lifecycle are two axes. <c>[Relation.*]</c> says how a row is linked;
///         <c>[PartOf]</c> says who writes it. This is where the two meet: without it, a part with no
///         relation to its whole would produce nothing and nobody would say so.
///     </para>
///     <para>
///         The edge may be declared on either end — the child's <c>ManyToOne</c>, or the parent's
///         collection or reference back to the child. With several edges to the parent, <c>Via</c>
///         names the one meant, the way <c>Inverse</c> already works.
///     </para>
///     <para>
///         ⚠️ Cascade is required only for an <b>exclusive</b> part. <c>Exclusive = false</c> keeps
///         «the parent may write it» and drops «it has no life of its own»: the conformance
///         <c>DeliveryAddress</c> is written through its order, addressable alone, and detachable — its
///         row outlives the link by design, and the key sits on the order. Demanding a cascade there
///         would contradict the declaration it exists to allow.
///     </para>
/// </remarks>
internal static partial class RelationValidator
{
    private const string PartOfAttributeName = "PartOfAttribute";
    private const string EntityAttributesNamespace = "Pragmatic.Persistence.Entity";

    private static void ValidatePartOf(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var attribute in entity.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: PartOfAttributeName, TypeArguments.Length: 1 } declaration
                || declaration.ContainingNamespace?.ToDisplayString() != EntityAttributesNamespace)
                continue;

            if (declaration.TypeArguments[0] is not INamedTypeSymbol parent)
                continue;

            var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
            var via = NamedString(attribute, "Via");
            var exclusive = NamedBool(attribute, "Exclusive") ?? true;

            var ownEdges = relations
                .Where(r => r.Info.RelationType == "ManyToOne"
                            && SymbolEqualityComparer.Default.Equals(r.Info.TargetType, parent))
                .ToList();
            var parentEdges = RelationDetection.GetRelations(parent)
                .Select(pair => pair.Item2)
                .Where(info => SymbolEqualityComparer.Default.Equals(info.TargetType, entity))
                .ToList();

            if (ownEdges.Count == 0 && parentEdges.Count == 0)
            {
                diagnostics.Add(Create(RelationDiagnosticKind.PartOfWithoutRelation, location,
                    entity.Name, parent.Name));
                continue;
            }

            var edge = ResolveOwnershipEdge(ownEdges, parentEdges, via, entity, parent, location, diagnostics);
            if (edge is null || !exclusive)
                continue;

            var onDelete = EffectiveOnDelete(edge.Value, entity, parentEdges);
            if (onDelete != "Cascade")
            {
                diagnostics.Add(Create(RelationDiagnosticKind.PartOfEdgeDoesNotCascade, location,
                    entity.Name, parent.Name, edge.Value.Navigation, onDelete));
            }
        }
    }

    /// <summary>An ownership edge: its navigation name on this entity, and the declaration that carries it.</summary>
    private readonly record struct OwnershipEdge(string Navigation, RelationInfo Info, bool DeclaredOnParent);

    /// <summary>The edge that carries the ownership, or <c>null</c> with the reason reported.</summary>
    private static OwnershipEdge? ResolveOwnershipEdge(
        List<Relation> ownEdges,
        List<RelationInfo> parentEdges,
        string? via,
        INamedTypeSymbol entity,
        INamedTypeSymbol parent,
        LocationInfo? location,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        // The candidates, named as this entity sees them: its own navigations, or the navigation the
        // parent's collection generates on it.
        var candidates = new List<OwnershipEdge>();
        foreach (var own in ownEdges)
            candidates.Add(new OwnershipEdge(own.NavigationName, own.Info, false));
        if (ownEdges.Count == 0)
            foreach (var theirs in parentEdges)
                candidates.Add(new OwnershipEdge(theirs.InverseProperty ?? parent.Name, theirs, true));

        var names = string.Join(", ", candidates.Select(c => c.Navigation));

        if (!string.IsNullOrEmpty(via))
        {
            foreach (var candidate in candidates)
                if (candidate.Navigation == via)
                    return candidate;

            diagnostics.Add(Create(RelationDiagnosticKind.PartOfViaNotFound, location,
                entity.Name, parent.Name, via!, names));
            return null;
        }

        if (candidates.Count == 1)
            return candidates[0];

        diagnostics.Add(Create(RelationDiagnosticKind.PartOfViaRequired, location,
            entity.Name, parent.Name, names));
        return null;
    }

    /// <summary>
    ///     What the ownership edge resolves to on delete: the parent's collection when it declares one
    ///     — the side that owns the relationship — else the child's own declaration. A key that sits on
    ///     the parent (the parent references the child) cannot cascade towards the child at all.
    /// </summary>
    private static string EffectiveOnDelete(OwnershipEdge edge, INamedTypeSymbol entity, List<RelationInfo> parentEdges)
    {
        if (edge.DeclaredOnParent)
            return edge.Info.RelationType == "ManyToOne"
                ? "a key on the parent's side, which cannot cascade to the part"
                : edge.Info.OnDelete;

        RelationInfo? owning = null;
        var seen = 0;
        foreach (var info in parentEdges)
        {
            if (info.RelationType != "OneToMany")
                continue;
            seen++;
            if (seen == 1 || string.Equals(info.InverseProperty, edge.Navigation, StringComparison.Ordinal))
                owning = info;
        }

        return owning?.OnDelete ?? edge.Info.OnDelete;
    }

    private static string? NamedString(AttributeData attribute, string name)
    {
        foreach (var named in attribute.NamedArguments)
            if (named.Key == name)
                return named.Value.Value as string;
        return null;
    }

    private static bool? NamedBool(AttributeData attribute, string name)
    {
        foreach (var named in attribute.NamedArguments)
            if (named.Key == name && named.Value.Value is bool value)
                return value;
        return null;
    }
}
