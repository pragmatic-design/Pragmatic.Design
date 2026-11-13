using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     What a relation's <c>OnSoftDelete</c> asks of its dependent end (PRAG0640, PRAG0641).
/// </summary>
internal static partial class RelationValidator
{
    private const string SoftDeleteInterface = "Pragmatic.Persistence.Entity.ISoftDelete";

    /// <summary>
    ///     <c>SetNull</c> needs a key that can be null, and <c>Cascade</c> a dependent that can be soft-deleted.
    /// </summary>
    /// <remarks>
    ///     Both are read on the dependent, which is this entity for a <c>ManyToOne</c>, the target for a
    ///     <c>OneToMany</c>, and whichever end is not the principal for a <c>OneToOne</c>. The key's nullability is
    ///     the dependent's to state: a parent's <c>OneToMany</c> reads it from the child's <c>ManyToOne</c>, and with
    ///     none the key is required, as the generated one is.
    /// </remarks>
    private static void ValidateOnSoftDelete(
        INamedTypeSymbol entity,
        List<Relation> relations,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var relation in relations)
        {
            if (relation.Info.OnSoftDelete is not { } behavior)
                continue;

            var dependentEnd = DependentEnd(entity, relation.Info);
            if (dependentEnd is null)
                continue;

            var (dependent, principal, isRequired) = dependentEnd.Value;

            if (behavior is "SetNull" or "ClientSetNull" && isRequired)
                diagnostics.Add(Create(
                    RelationDiagnosticKind.SoftDeleteSetNullOnRequiredKey,
                    relation.Location,
                    dependent.Name,
                    principal.Name));

            if (behavior == "Cascade" && !IsSoftDeletable(dependent))
                diagnostics.Add(Create(
                    RelationDiagnosticKind.SoftDeleteCascadeToHardEntity,
                    relation.Location,
                    dependent.Name,
                    principal.Name));
        }
    }

    /// <summary>The dependent, the principal, and whether the dependent's key is required.</summary>
    private static (INamedTypeSymbol Dependent, INamedTypeSymbol Principal, bool IsRequired)? DependentEnd(
        INamedTypeSymbol entity, RelationInfo info)
    {
        var target = info.TargetType;

        switch (info.RelationType)
        {
            case "ManyToOne":
                return (entity, target, info.IsRequired);

            case "OneToMany":
                return (target, entity, ChildDeclarationOf(target, entity, info)?.IsRequired ?? true);

            case "OneToOne" when info.IsPrincipal:
                return (target, entity, OneToOneBackTo(target, entity)?.IsRequired ?? true);

            case "OneToOne":
                return (entity, target, info.IsRequired);

            default:
                return null;
        }
    }

    /// <summary>The child's <c>ManyToOne</c> back to <paramref name="parent" />, paired by name when there are several.</summary>
    private static RelationInfo? ChildDeclarationOf(INamedTypeSymbol child, INamedTypeSymbol parent, RelationInfo parentsDeclaration)
    {
        var candidates = new List<RelationInfo>();
        foreach (var (_, info) in RelationDetection.GetRelations(child))
            if (info.RelationType == "ManyToOne" && SymbolEqualityComparer.Default.Equals(info.TargetType, parent))
                candidates.Add(info);

        if (candidates.Count <= 1)
            return candidates.FirstOrDefault();

        var parentsNavigation = RelationDetection.GetNavigationName(parentsDeclaration);
        return candidates.FirstOrDefault(c =>
            (parentsDeclaration.InverseProperty is not null
             && RelationDetection.GetNavigationName(c) == parentsDeclaration.InverseProperty)
            || c.InverseProperty == parentsNavigation);
    }

    /// <summary>Declared <c>[SoftDelete]</c>, or implementing the interface by hand.</summary>
    private static bool IsSoftDeletable(INamedTypeSymbol type)
        => TraitDetector.Detect(type).IsSoftDelete
           || type.AllInterfaces.Any(i => i.ToDisplayString() == SoftDeleteInterface);
}
