using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The symmetric form: two collections and one join between them.
/// </summary>
internal static partial class RelationGraphBuilder
{

    /// <summary>
    ///     ManyToMany: Both sides get collections.
    /// </summary>
    private static void ProcessManyToMany(
        EntityMetadataModel owner,
        RelationAttributeModel rel,
        EntityMetadataModel? targetEntity,
        bool isCrossBoundary,
        Dictionary<string, List<NavigationMetadataModel>> navsPerEntity,
        Dictionary<string, List<GeneratedRelationPropertyModel>> propsPerEntity)
    {
        var collectionName = RelationNavigationNaming.Collection(rel.NavigationName, rel.TargetTypeName);

        // One relationship, one join. Read from each declaration on its own, two ends naming
        // different tables produced two of them, and keys named on one end never reached the
        // configuration generated from the other.
        var counterpart = RelationPairing.Counterpart(
            targetEntity, owner.FullTypeName, "ManyToMany", rel);
        var join = RelationPairing.ResolveManyToMany(
            owner.FullTypeName, rel, rel.TargetTypeFullName, counterpart);

        // The collection on the other end: named by that end if it declares one, else by this end's
        // Inverse, else after this type. Computed before the owner's navigation is emitted, because
        // that navigation has to declare it to EF — left null, EntityConfigurationTemplate emitted a
        // bare `.WithMany()` for a member the generator had just created.
        var inverseCollectionName = counterpart is not null
            ? RelationNavigationNaming.Collection(counterpart.NavigationName, owner.TypeName)
            : rel.InverseProperty ?? StringHelper.Pluralize(owner.TypeName);
        var inverseExists = targetEntity is not null && !isCrossBoundary;

        // Owner gets: collection
        AddNavigation(navsPerEntity, owner.FullTypeName, new NavigationMetadataModel
        {
            Name = collectionName,
            TargetTypeName = rel.TargetTypeFullName,
            TargetFullTypeName = rel.TargetTypeFullName,
            NavigationType = "ManyToMany",
            InverseProperty = inverseExists ? inverseCollectionName : null,
            JoinTable = join.JoinTable,
            JoinEntityTypeName = join.JoinEntityTypeName,
            JoinLeftKey = join.LeftKey,
            JoinRightKey = join.RightKey,
            TargetBoundaryTypeFullName = rel.TargetBoundaryTypeFullName,
            IsFromRelationAttribute = true
        });

        AddGeneratedProperty(propsPerEntity, owner.FullTypeName, new GeneratedRelationPropertyModel
        {
            Kind = RelationPropertyKind.Collection,
            Name = collectionName,
            TypeName = rel.TargetTypeName,
            FullTypeName = rel.TargetTypeFullName,
            Summary = $"Many-to-many collection of {rel.TargetTypeName} entities."
        });

        // Target gets: inverse collection, on the same terms as every other form — the other end of a
        // declared relationship receives its member.
        //
        // ⚠️ Not conditional on `targetEntity.UsesRelationAttributes`: that would make whether THIS
        // relationship produces its inverse depend on whether the target declares some OTHER,
        // unrelated relation, and adding an unrelated [Relation.*] to an entity would make a
        // collection appear on it as a side effect.
        if (targetEntity is not null && !isCrossBoundary)
        {
            // Only add if the target doesn't already declare its own ManyToMany back
            var targetAlreadyDeclares = targetEntity.RelationAttributes
                .Any(r => r.RelationType == "ManyToMany" &&
                          r.TargetTypeName == owner.TypeName);

            if (!targetAlreadyDeclares)
            {
                AddNavigation(navsPerEntity, targetEntity.FullTypeName, new NavigationMetadataModel
                {
                    Name = inverseCollectionName,
                    TargetTypeName = owner.FullTypeName,
                    TargetFullTypeName = owner.FullTypeName,
                    NavigationType = "ManyToMany",
                    InverseProperty = collectionName,
                    JoinTable = join.JoinTable,
                    JoinEntityTypeName = join.JoinEntityTypeName,
                    // Seen from the target the two ends of the join are the other way round.
                    JoinLeftKey = join.RightKey,
                    JoinRightKey = join.LeftKey,
                    TargetBoundaryTypeFullName = owner.BoundaryTypeFullName,
                    IsFromRelationAttribute = true
                });

                AddGeneratedProperty(propsPerEntity, targetEntity.FullTypeName,
                    new GeneratedRelationPropertyModel
                    {
                        Kind = RelationPropertyKind.Collection,
                        Name = inverseCollectionName,
                        TypeName = owner.TypeName,
                        FullTypeName = owner.FullTypeName,
                        Summary = $"Many-to-many collection of {owner.TypeName} entities."
                    });
            }
        }
    }
}
