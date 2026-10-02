using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     Which end of a relationship carries what: the key, the role, and the options that
///     describe them.
/// </summary>
internal static partial class RelationValidator
{
    // =========================================================================
    // PRAG0616 — a join entity whose foreign keys nobody named
    // =========================================================================

    /// <summary>
    ///     A many-to-many with an explicit join entity has to say which of its properties hold the two
    ///     keys.
    /// </summary>
    /// <remarks>
    ///     EF Core otherwise binds shadow foreign keys named after the navigation, and the migration
    ///     creates only the properties the join entity declares: the table exists with one set of
    ///     columns and the model looks for another. Nothing fails until the first write through the
    ///     navigation.
    /// </remarks>
    /// <summary>Whether the join entity declares a ManyToOne to each end.</summary>
    private static bool DeclaresBothEnds(
        INamedTypeSymbol? joinEntity, INamedTypeSymbol left, INamedTypeSymbol right)
    {
        if (joinEntity is null)
            return false;

        var toLeft = false;
        var toRight = false;

        foreach (var attr in joinEntity.GetAttributes())
        {
            var info = RelationDetection.ClassifyRelationAttribute(attr);
            if (info is null || info.RelationType != "ManyToOne")
                continue;

            toLeft |= SymbolEqualityComparer.Default.Equals(info.TargetType, left);
            toRight |= SymbolEqualityComparer.Default.Equals(info.TargetType, right);
        }

        return toLeft && toRight;
    }

    private static void ValidateJoinEntityKeys(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var relation in relations)
        {
            if (relation.Info.JoinEntityType is null)
                continue;
            if (!string.IsNullOrEmpty(relation.Info.LeftKey) && !string.IsNullOrEmpty(relation.Info.RightKey))
                continue;

            // A join entity that declares its own ManyToOne to both ends already has the two foreign
            // keys, with names the transform reproduces. Asking for LeftKey/RightKey there would be
            // asking the author to repeat themselves — and the same rule has to hold in both places,
            // or this reports what the generator resolves.
            if (DeclaresBothEnds(relation.Info.JoinEntityType, entity, relation.Info.TargetType))
                continue;

            diagnostics.Add(Create(
                RelationDiagnosticKind.JoinEntityKeysNotNamed,
                relation.Location,
                entity.Name,
                relation.Info.TargetType.Name,
                relation.Info.JoinEntityType.Name));
        }
    }

    // =========================================================================
    // PRAG0618 — a one-to-one has exactly one principal
    // =========================================================================

    /// <summary>
    ///     Both ends of a one-to-one declare the same attribute, so only <c>IsPrincipal</c> tells them
    ///     apart, and exactly one end must say it.
    /// </summary>
    /// <remarks>
    ///     Neither end saying it made both behave as the dependent, and the relationship got two
    ///     foreign keys, one per table. Both saying it left it with none. A lone declaration calling
    ///     itself principal is the second case again: there is no other declaration to hold the key.
    /// </remarks>
    private static void ValidateOneToOnePrincipal(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var relation in relations)
        {
            if (relation.Info.RelationType != "OneToOne")
                continue;

            var target = relation.Info.TargetType;
            var theirs = OneToOneBackTo(target, entity);
            var problem = DescribeThePrincipals(relation.Info.IsPrincipal, theirs);
            if (problem is null)
                continue;

            diagnostics.Add(Create(
                RelationDiagnosticKind.OneToOnePrincipalNotDecided,
                relation.Location,
                entity.Name,
                target.Name,
                problem));
        }
    }

    /// <summary>What is wrong with the pair's principals, or <c>null</c> when exactly one claims it.</summary>
    private static string? DescribeThePrincipals(bool minePrincipal, RelationInfo? theirs)
    {
        if (theirs is null)
            return minePrincipal
                ? "declares itself the principal and is the only declaration, so nothing carries the "
                  + "foreign key"
                : null;

        return (minePrincipal, theirs.IsPrincipal) switch
        {
            (true, true) => "is declared principal from both ends, so neither carries the foreign key",
            (false, false) => "names no principal, so both ends would carry a foreign key",
            _ => null
        };
    }

    /// <summary>The other end's <c>OneToOne</c> back to this entity, when it declares one.</summary>
    private static RelationInfo? OneToOneBackTo(INamedTypeSymbol other, INamedTypeSymbol entity)
    {
        foreach (var (_, info) in RelationDetection.GetRelations(other))
            if (info.RelationType == "OneToOne"
                && SymbolEqualityComparer.Default.Equals(info.TargetType, entity))
                return info;

        return null;
    }

    // =========================================================================
    // PRAG0611 — the delete behaviour, on the side that owns the relationship
    // =========================================================================

    /// <summary>
    ///     A <c>ManyToOne</c> may not set <c>OnDelete</c> when the target declares the collection that
    ///     owns the relationship.
    /// </summary>
    /// <remarks>
    ///     <c>RelationGraphBuilder</c> reads the behaviour from the owning side, so that both generated
    ///     configurations agree instead of depending on the order the graph walked the entities in. A
    ///     value written here is therefore read by nobody, and this is what keeps that from being a
    ///     silent no-op. Where there is no counterpart the declaration is the only source there is, and
    ///     setting it is correct — that is the unidirectional <c>ManyToOne</c>.
    /// </remarks>
    private static void ValidateDeleteBehaviourOwnership(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var relation in relations)
        {
            if (relation.Info.RelationType != "ManyToOne" || !relation.DeclaresOnDelete)
                continue;

            if (!DeclaresOwningCollectionBackTo(relation.Info.TargetType, entity))
                continue;

            diagnostics.Add(Create(
                RelationDiagnosticKind.DeleteBehaviourNotOwned,
                relation.Location,
                entity.Name,
                relation.Info.TargetType.Name));
        }
    }

    /// <summary>Whether <paramref name="target" /> declares a collection of <paramref name="entity" />.</summary>
    private static bool DeclaresOwningCollectionBackTo(INamedTypeSymbol target, INamedTypeSymbol entity)
    {
        foreach (var (_, info) in RelationDetection.GetRelations(target))
            if (info.RelationType == "OneToMany"
                && SymbolEqualityComparer.Default.Equals(info.TargetType, entity))
                return true;

        return false;
    }

    /// <summary>Whether the form emits a member on the entity it points at.</summary>
    private static bool WritesOnTarget(string relationType)
        => relationType is "OneToMany" or "OneToOne" or "ManyToMany";

    /// <summary>The name the convention would give the inverse — what the message shows.</summary>
    private static string DefaultInverseName(string ownerName, string relationType)
        => relationType == "OneToMany" ? ownerName : StringHelper.Pluralize(ownerName);
}
