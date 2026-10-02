using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Parses [Relation.*] attributes from an entity type symbol into RelationAttributeModels.
/// </summary>
internal static class RelationTransform
{
    /// <summary>
    ///     Collects all [Relation.*] attributes from the given entity type.
    /// </summary>
    public static ImmutableArray<RelationAttributeModel> CollectRelationAttributes(INamedTypeSymbol typeSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<RelationAttributeModel>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var info = RelationDetection.ClassifyRelationAttribute(attr);
            if (info is null)
                continue;

            // Resolve target entity boundary for cross-boundary detection
            var targetBoundary = FindBelongsToAttribute(info.TargetType);

            // Derive navigation name from convention if not explicit
            var navName = RelationDetection.GetNavigationName(info);

            // Derive FK name
            var fkName = info.ForeignKeyProperty ?? DeriveForeignKeyName(info, typeSymbol.Name);

            builder.Add(new RelationAttributeModel
            {
                RelationType = info.RelationType,
                TargetTypeFullName = info.TargetType.ToDisplayString(),
                TargetTypeName = info.TargetType.Name,
                NavigationName = navName,
                InverseProperty = info.InverseProperty,
                ForeignKeyProperty = fkName,
                OnDelete = info.OnDelete,
                IsRequired = info.IsRequired,
                IsPrincipal = info.IsPrincipal,
                JoinTable = info.JoinTable,
                JoinEntityTypeName = info.JoinEntityType?.ToDisplayString(),
                JoinLeftKey = info.LeftKey ?? InferJoinKey(info.JoinEntityType, typeSymbol),
                JoinRightKey = info.RightKey ?? InferJoinKey(info.JoinEntityType, info.TargetType),
                TargetBoundaryTypeFullName = targetBoundary,
                IsExplicit = info.IsWithNavigation
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The join entity's foreign key to <paramref name="end" />, when its own relations name one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A join entity that declares <c>[Relation.ManyToOne&lt;Left&gt;]</c> and
    ///         <c>[Relation.ManyToOne&lt;Right&gt;]</c> already has both foreign keys generated, with
    ///         names this reproduces rather than asks for. Naming them by hand there would be asking
    ///         the author to repeat what they have already said.
    ///     </para>
    ///     <para>
    ///         When the join entity declares no such relation — it holds payload and two ids it wrote
    ///         itself, or it holds only payload — nothing here can guess which property is which end,
    ///         least of all on a self-reference where both are the same type. That is what
    ///         <c>LeftKey</c>/<c>RightKey</c> are for, and PRAG0616 asks for them.
    ///     </para>
    /// </remarks>
    private static string? InferJoinKey(INamedTypeSymbol? joinEntity, INamedTypeSymbol end)
    {
        if (joinEntity is null)
            return null;

        foreach (var attr in joinEntity.GetAttributes())
        {
            var info = RelationDetection.ClassifyRelationAttribute(attr);
            if (info is null || info.RelationType != "ManyToOne")
                continue;
            if (!SymbolEqualityComparer.Default.Equals(info.TargetType, end))
                continue;

            return info.ForeignKeyProperty ?? DeriveForeignKeyName(info, joinEntity.Name);
        }

        return null;
    }

    /// <summary>
    ///     Derives the foreign key property name from convention.
    /// </summary>
    private static string DeriveForeignKeyName(RelationInfo info, string ownerTypeName)
    {
        return info.RelationType switch
        {
            // ManyToOne: FK on this entity, named after the navigation
            "ManyToOne" => (info.NavigationName ?? info.TargetType.Name) + "Id",
            // OneToOne: FK on this entity (dependent side)
            "OneToOne" => (info.NavigationName ?? info.TargetType.Name) + "Id",
            // OneToMany: FK goes on the child entity, named after the inverse or this entity
            "OneToMany" => (info.InverseProperty ?? ownerTypeName) + "Id",
            _ => info.TargetType.Name + "Id"
        };
    }

    /// <summary>
    ///     Finds [BelongsTo&lt;T&gt;] on the entity type to determine its boundary.
    /// </summary>
    public static string? FindBelongsToAttribute(INamedTypeSymbol symbol)
        => BoundaryOwnershipReader.BoundaryOf(symbol).FullTypeName;
}
