using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The parent/child forms: the collection on one side, the foreign key on the other.
/// </summary>
internal static partial class RelationGraphBuilder
{

    /// <summary>
    ///     OneToMany: Owner (parent) gets collection, target (child) gets FK + ref nav.
    /// </summary>
    private static void ProcessOneToMany(
        EntityMetadataModel owner,
        RelationAttributeModel rel,
        EntityMetadataModel? targetEntity,
        bool isCrossBoundary,
        Dictionary<string, List<NavigationMetadataModel>> navsPerEntity,
        Dictionary<string, List<GeneratedRelationPropertyModel>> propsPerEntity)
    {
        var collectionName = RelationNavigationNaming.Collection(rel.NavigationName, rel.TargetTypeName);

        // The child may describe its own end. Reading it here — instead of deriving a second answer
        // and letting the two be reconciled by member name — is what keeps one relationship from
        // becoming two columns, or from losing whichever option was processed second.
        var counterpart = RelationPairing.Counterpart(
            targetEntity, owner.FullTypeName, "ManyToOne", rel);
        var resolved = RelationPairing.Resolve(owner.TypeName, rel, counterpart);

        var childNavName = resolved.ChildNavigationName;
        var childFkName = resolved.ForeignKeyName;

        // Owner gets: collection navigation
        AddNavigation(navsPerEntity, owner.FullTypeName, new NavigationMetadataModel
        {
            Name = collectionName,
            TargetTypeName = rel.TargetTypeFullName,
            TargetFullTypeName = rel.TargetTypeFullName,
            NavigationType = "OneToMany",
            InverseProperty = isCrossBoundary ? null : childNavName,
            ForeignKeyProperty = childFkName,
            OnDelete = rel.OnDelete,
            TargetBoundaryTypeFullName = rel.TargetBoundaryTypeFullName,
            IsFromRelationAttribute = true
        });

        AddGeneratedProperty(propsPerEntity, owner.FullTypeName, new GeneratedRelationPropertyModel
        {
            Kind = RelationPropertyKind.Collection,
            Name = collectionName,
            TypeName = rel.TargetTypeName,
            FullTypeName = rel.TargetTypeFullName,
            Summary = $"Collection of {rel.TargetTypeName} entities."
        });

        // Target (child) gets: FK + ref nav (if same boundary and target is a known entity)
        if (targetEntity is not null)
        {
            // FK on child. Its nullability is the child's to state: a parent that declares the
            // collection says nothing about whether its children must have one.
            var fkType = RelationForeignKeyNaming.Type(owner.IdType, resolved.IsRequired);
            AddGeneratedProperty(propsPerEntity, targetEntity.FullTypeName, new GeneratedRelationPropertyModel
            {
                Kind = RelationPropertyKind.ForeignKey,
                Name = childFkName,
                TypeName = fkType,
                FullTypeName = fkType,
                IsCrossBoundary = false,
                Summary = $"FK to {owner.TypeName}."
            });

            if (!isCrossBoundary)
            {
                // Ref nav on child
                AddGeneratedProperty(propsPerEntity, targetEntity.FullTypeName, new GeneratedRelationPropertyModel
                {
                    Kind = RelationPropertyKind.ReferenceNav,
                    Name = childNavName,
                    TypeName = owner.TypeName,
                    FullTypeName = owner.FullTypeName,
                    IsOptional = !resolved.IsRequired,
                    Summary = $"Navigation to parent {owner.TypeName}."
                });

                // Navigation metadata on child (ManyToOne side)
                AddNavigation(navsPerEntity, targetEntity.FullTypeName, new NavigationMetadataModel
                {
                    Name = childNavName,
                    TargetTypeName = owner.FullTypeName,
                    TargetFullTypeName = owner.FullTypeName,
                    NavigationType = "ManyToOne",
                    InverseProperty = collectionName,
                    ForeignKeyProperty = childFkName,
                    OnDelete = resolved.OnDelete,
                    IsRequired = resolved.IsRequired,
                    TargetBoundaryTypeFullName = owner.BoundaryTypeFullName,
                    IsFromRelationAttribute = true
                });
            }
        }
    }

    /// <summary>
    ///     ManyToOne: Owner (child) gets FK + optional ref nav.
    /// </summary>
    private static void ProcessManyToOne(
        EntityMetadataModel owner,
        RelationAttributeModel rel,
        EntityMetadataModel? targetEntity,
        bool isCrossBoundary,
        Dictionary<string, List<NavigationMetadataModel>> navsPerEntity,
        Dictionary<string, List<GeneratedRelationPropertyModel>> propsPerEntity)
    {
        var idType = targetEntity?.IdType ?? "System.Guid";

        // The same resolution the parent's OneToMany reaches, from the other end: both are handed the
        // same two declarations, so both emit the same members for this relationship.
        var counterpart = RelationPairing.Counterpart(
            targetEntity, owner.FullTypeName, "OneToMany", rel);
        var resolved = RelationPairing.Resolve(rel.TargetTypeName, counterpart, rel);

        var navName = resolved.ChildNavigationName;
        var onDelete = resolved.OnDelete;

        // RelationForeignKeyNaming, not the rule inline: TraitPropertyResolver has to predict the very
        // same name and type for the features that run before this property exists.
        var fkName = resolved.ForeignKeyName;
        var fkType = RelationForeignKeyNaming.Type(idType, resolved.IsRequired);

        // Owner gets: FK property
        AddGeneratedProperty(propsPerEntity, owner.FullTypeName, new GeneratedRelationPropertyModel
        {
            Kind = RelationPropertyKind.ForeignKey,
            Name = fkName,
            TypeName = fkType,
            FullTypeName = fkType,
            IsCrossBoundary = isCrossBoundary,
            Summary = isCrossBoundary
                ? $"FK to {rel.TargetTypeName} (cross-boundary)."
                : $"FK to {rel.TargetTypeName}."
        });

        if (!isCrossBoundary)
        {
            // A [Lookup] target keeps its key and constraint, but the reference member is the lookup
            // feature's: it emits one of the same name, [NotMapped] and resolved from its cache, so
            // generating a navigation here would put the same member in two partial parts (CS0102).
            var isLookupReference = targetEntity is { IsLookup: true };

            if (!isLookupReference)
            {
                // Same boundary: also add ref nav
                AddGeneratedProperty(propsPerEntity, owner.FullTypeName, new GeneratedRelationPropertyModel
                {
                    Kind = RelationPropertyKind.ReferenceNav,
                    Name = navName,
                    TypeName = rel.TargetTypeName,
                    FullTypeName = rel.TargetTypeFullName,
                    IsOptional = !resolved.IsRequired,
                    Summary = $"Navigation to {rel.TargetTypeName}."
                });
            }

            // Navigation metadata
            AddNavigation(navsPerEntity, owner.FullTypeName, new NavigationMetadataModel
            {
                Name = navName,
                TargetTypeName = rel.TargetTypeFullName,
                TargetFullTypeName = rel.TargetTypeFullName,
                NavigationType = "ManyToOne",
                InverseProperty = rel.InverseProperty,
                ForeignKeyProperty = fkName,
                OnDelete = onDelete,
                IsRequired = resolved.IsRequired,
                TargetBoundaryTypeFullName = rel.TargetBoundaryTypeFullName,
                IsFromRelationAttribute = true,
                IsLookupReference = isLookupReference
            });
        }
        else
        {
            // Cross-boundary: navigation metadata with just FK info (for EntityConfiguration)
            AddNavigation(navsPerEntity, owner.FullTypeName, new NavigationMetadataModel
            {
                Name = navName,
                TargetTypeName = rel.TargetTypeFullName,
                TargetFullTypeName = rel.TargetTypeFullName,
                NavigationType = "ManyToOne",
                ForeignKeyProperty = fkName,
                OnDelete = onDelete,
                IsRequired = resolved.IsRequired,
                TargetBoundaryTypeFullName = rel.TargetBoundaryTypeFullName,
                IsFromRelationAttribute = true
            });
        }
    }
}
