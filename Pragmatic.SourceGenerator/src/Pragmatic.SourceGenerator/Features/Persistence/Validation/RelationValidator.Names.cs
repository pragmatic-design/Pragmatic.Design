using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     What each member is called: the rules that keep two declarations from naming one member
///     twice, or naming one that does not exist.
/// </summary>
internal static partial class RelationValidator
{
    // =========================================================================
    // PRAG0612 / PRAG0615 — navigation naming
    // =========================================================================

    /// <summary>
    ///     PRAG0612: several relations to the same target where at least one is unnamed — the
    ///     convention cannot tell them apart. PRAG0615: two relations, whatever their target,
    ///     resolving to the same navigation name.
    /// </summary>
    private static void ValidateAmbiguity(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        // PRAG0612 first: when the collision comes from unnamed relations to one target, "add
        // WithNavigation" is the actionable message — reporting a duplicate name on top would be noise.
        var ambiguous = new HashSet<int>();

        foreach (var group in GroupBy(relations, r => r.Info.TargetType.ToDisplayString()))
        {
            if (group.Count < 2 || group.All(r => r.Info.NavigationName is not null))
                continue;

            foreach (var relation in group.Where(r => r.Info.NavigationName is null))
            {
                ambiguous.Add(relation.Index);
                diagnostics.Add(Create(
                    RelationDiagnosticKind.AmbiguousRelation,
                    relation.Location,
                    entity.Name,
                    group.Count.ToString(),
                    relation.Info.TargetType.Name,
                    relation.NavigationName,
                    relation.Info.RelationType));
            }
        }

        foreach (var group in GroupBy(relations, r => r.NavigationName))
        {
            if (group.Count < 2 || group.Any(r => ambiguous.Contains(r.Index)))
                continue;

            var first = group[0];
            foreach (var duplicate in group.Skip(1))
            {
                diagnostics.Add(Create(
                    RelationDiagnosticKind.DuplicateNavigationName,
                    duplicate.Location,
                    entity.Name,
                    duplicate.NavigationName,
                    first.Info.TargetType.Name,
                    duplicate.Info.TargetType.Name,
                    duplicate.Info.RelationType));
            }
        }
    }

    // =========================================================================
    // PRAG0617 — the inverse name, where the convention cannot tell two apart
    // =========================================================================

    /// <summary>
    ///     Among relations to one target, those that write a member on it must name the inverse when
    ///     there is more than one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>PRAG0612</c> guards the name on the declaring side and falls silent once every one is
    ///         written. This is the other half: the inverse name has its own default — the declaring
    ///         entity, pluralised for a collection — so two relations to the same type land on the same
    ///         member of the target and <c>RelationGraphBuilder</c> keeps the first.
    ///     </para>
    ///     <para>
    ///         <c>ManyToOne</c> is excluded because it writes nothing on the target: two of them to one
    ///         type cannot collide there, which is why a pair like
    ///         <c>AssignedGuest</c>/<c>RequestedBy</c> is correct without an inverse.
    ///     </para>
    /// </remarks>
    private static void ValidateInverseNames(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var group in GroupBy(relations, r => r.Info.TargetType.ToDisplayString()))
        {
            var writing = group.Where(r => WritesOnTarget(r.Info.RelationType)).ToList();
            if (writing.Count < 2)
                continue;

            foreach (var relation in writing.Where(r => string.IsNullOrEmpty(r.Info.InverseProperty)))
            {
                diagnostics.Add(Create(
                    RelationDiagnosticKind.InverseNameNotDeclared,
                    relation.Location,
                    entity.Name,
                    writing.Count.ToString(),
                    relation.Info.TargetType.Name,
                    DefaultInverseName(entity.Name, relation.Info.RelationType)));
            }
        }
    }

    // =========================================================================
    // PRAG0610 — one member, one name
    // =========================================================================

    /// <summary>
    ///     A <c>OneToMany</c> whose <c>Inverse</c> names the child's navigation one way, while the
    ///     child's own declaration names it another.
    /// </summary>
    /// <remarks>
    ///     The relationship resolves once and the child's own name is preferred, so the
    ///     <c>Inverse</c> written here would produce nothing — the same silent no-op
    ///     <c>PRAG0611</c> exists to keep out. Reported on the parent, which is where the discarded
    ///     value is written.
    /// </remarks>
    private static void ValidateInverseAgreesWithTheChild(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var relation in relations)
        {
            if (relation.Info.RelationType != "OneToMany"
                || string.IsNullOrEmpty(relation.Info.InverseProperty))
                continue;

            // With several relations back to this entity, Inverse has to name ONE of them — that is
            // precisely how the pairs are told apart — so the contradiction is "none of them".
            var childNames = ChildNavigationNamesOn(relation.Info.TargetType, entity);
            if (childNames.Count == 0 || childNames.Contains(relation.Info.InverseProperty!))
                continue;

            diagnostics.Add(Create(
                RelationDiagnosticKind.InverseContradictsTheDeclaredName,
                relation.Location,
                entity.Name,
                relation.Info.TargetType.Name,
                relation.Info.InverseProperty!,
                string.Join(", ", childNames)));
        }
    }

    /// <summary>The navigation names the child writes for its own relations back to this entity.</summary>
    private static List<string> ChildNavigationNamesOn(INamedTypeSymbol child, INamedTypeSymbol parent)
    {
        var names = new List<string>();
        foreach (var (_, info) in RelationDetection.GetRelations(child))
            if (info.RelationType == "ManyToOne"
                && SymbolEqualityComparer.Default.Equals(info.TargetType, parent)
                && info.NavigationName is not null)
                names.Add(info.NavigationName);

        return names;
    }

    // =========================================================================
    // PRAG0613 / PRAG0614 — inverse navigation
    // =========================================================================

    private static void ValidateInverses(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var relation in relations)
        {
            var inverse = relation.Info.InverseProperty;
            if (string.IsNullOrEmpty(inverse))
                continue;

            var target = relation.Info.TargetType;

            // Only entities have navigations to resolve against; anything else is outside what the
            // relation graph models, and guessing there would produce false positives.
            if (!IsEntity(target))
                continue;

            // The generator creates the inverse navigation on the target itself for these relations,
            // so the name is a declaration, not a reference — nothing to resolve.
            if (GeneratesInverseOnTarget(entity, relation))
                continue;

            // A navigation declared in source (or already compiled into a referenced assembly).
            var declared = FindNavigationMember(target, inverse!);
            if (declared is not null)
            {
                var declaredType = NavigationElementType(declared);
                if (!PointsBackTo(declaredType, entity))
                {
                    diagnostics.Add(Create(
                        RelationDiagnosticKind.InversePropertyTypeMismatch,
                        relation.Location,
                        inverse!,
                        entity.Name,
                        target.Name,
                        declaredType?.Name ?? "?"));
                }

                continue;
            }

            // A navigation the target's own [Relation.*] will generate — invisible as a symbol during
            // this compilation, which is exactly why the attributes are consulted directly.
            var fromAttribute = FindRelationByNavigationName(target, inverse!);
            if (fromAttribute is null)
            {
                diagnostics.Add(Create(
                    RelationDiagnosticKind.InversePropertyNotFound,
                    relation.Location,
                    inverse!,
                    entity.Name,
                    target.Name,
                    OppositeRelationType(relation.Info.RelationType)));
            }
            else if (!PointsBackTo(fromAttribute.TargetType, entity))
            {
                diagnostics.Add(Create(
                    RelationDiagnosticKind.InversePropertyTypeMismatch,
                    relation.Location,
                    inverse!,
                    entity.Name,
                    target.Name,
                    fromAttribute.TargetType.Name));
            }
        }
    }

    /// <summary>
    ///     Whether RelationGraphBuilder generates the inverse navigation on the target for this
    ///     relation. Mirrors its Process* methods: when the generator writes the property itself,
    ///     <c>Inverse</c> only names what will be created and can never dangle.
    /// </summary>
    private static bool GeneratesInverseOnTarget(INamedTypeSymbol entity, Relation relation)
    {
        var target = relation.Info.TargetType;
        var crossBoundary = IsCrossBoundary(entity, target);

        return relation.Info.RelationType switch
        {
            // The child navigation IS the Inverse name and the generator writes it; across a boundary
            // the relation degrades to an FK and no inverse is configured either.
            "OneToMany" => true,
            // A ManyToOne never writes on the target — but across a boundary it configures no inverse.
            "ManyToOne" => crossBoundary,
            // Only the same-boundary dependent side writes the principal's navigation.
            "OneToOne" => !crossBoundary && !relation.Info.IsPrincipal,
            // The inverse collection is generated unless the target declares its own way back.
            "ManyToMany" => !crossBoundary &&
                            RelationDetection.HasRelationAttributes(target) &&
                            !DeclaresManyToManyBack(target, entity),
            _ => false
        };
    }

    private static bool DeclaresManyToManyBack(INamedTypeSymbol target, INamedTypeSymbol entity)
    {
        foreach (var (_, info) in RelationDetection.GetRelations(target))
        {
            if (info.RelationType == "ManyToMany" && info.TargetType.Name == entity.Name)
                return true;
        }

        return false;
    }
}
