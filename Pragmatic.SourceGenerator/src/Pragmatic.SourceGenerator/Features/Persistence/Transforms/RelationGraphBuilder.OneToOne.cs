using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The one-to-one: which end is the principal, and which one carries the key.
/// </summary>
internal static partial class RelationGraphBuilder
{

    /// <summary>
    ///     OneToOne: Dependent (declaring side) gets FK + ref nav. Principal gets ref nav.
    /// </summary>
    private static void ProcessOneToOne(
        EntityMetadataModel owner,
        RelationAttributeModel rel,
        EntityMetadataModel? targetEntity,
        bool isCrossBoundary,
        Dictionary<string, List<NavigationMetadataModel>> navsPerEntity,
        Dictionary<string, List<GeneratedRelationPropertyModel>> propsPerEntity)
    {
        var navName = RelationNavigationNaming.Reference(rel.NavigationName, rel.TargetTypeName);

        // Both ends declare the same attribute, so the roles come from IsPrincipal and have to be
        // decided for the pair. While each end decided for itself, a one-to-one nobody claimed to be
        // principal of got two foreign keys, and one both claimed got none.
        var counterpart = RelationPairing.Counterpart(
            targetEntity, owner.FullTypeName, "OneToOne", rel);
        var resolved = RelationPairing.ResolveOneToOne(owner, rel, targetEntity, counterpart);
        var isPrincipal = resolved.PrincipalFullTypeName == owner.FullTypeName;

        if (isPrincipal)
        {
            // Principal side: ref nav only, no FK, no config
            AddGeneratedProperty(propsPerEntity, owner.FullTypeName, new GeneratedRelationPropertyModel
            {
                Kind = RelationPropertyKind.ReferenceNav,
                Name = navName,
                TypeName = rel.TargetTypeName,
                FullTypeName = rel.TargetTypeFullName,
                Summary = $"Navigation to dependent {rel.TargetTypeName}."
            });

            AddNavigation(navsPerEntity, owner.FullTypeName, new NavigationMetadataModel
            {
                Name = navName,
                TargetTypeName = rel.TargetTypeFullName,
                TargetFullTypeName = rel.TargetTypeFullName,
                NavigationType = "OneToOne",
                InverseProperty = rel.InverseProperty,
                IsPrincipal = true,
                TargetBoundaryTypeFullName = rel.TargetBoundaryTypeFullName,
                IsFromRelationAttribute = true
            });

            return;
        }

        // The principal's navigation is the principal's own member: it names it, or this end names
        // it with Inverse, or it is called after this type. Computed here, before the dependent's
        // navigation is emitted, because that navigation has to declare it.
        var principalNavName = counterpart?.NavigationName
                               ?? rel.InverseProperty
                               ?? owner.TypeName;

        // Whether that member is going to exist at all: across a boundary only the foreign key is
        // generated, so there is no navigation for EF to be told about.
        var principalNavExists = targetEntity is not null && !isCrossBoundary;

        // Dependent side: FK + ref nav + config. RelationForeignKeyNaming, not the rule spelled out
        // again here: TraitPropertyResolver predicts this very name for the features that run before
        // the property exists, and a second copy of the rule is a second answer waiting to diverge.
        var fkName = resolved.ForeignKeyName;
        var fkType = RelationForeignKeyNaming.Type(
            targetEntity?.IdType ?? "System.Guid", resolved.IsRequired);

        AddGeneratedProperty(propsPerEntity, owner.FullTypeName, new GeneratedRelationPropertyModel
        {
            Kind = RelationPropertyKind.ForeignKey,
            Name = fkName,
            TypeName = fkType,
            FullTypeName = fkType,
            IsOptional = !resolved.IsRequired,
            Summary = $"FK to {rel.TargetTypeName}."
        });

        AddGeneratedProperty(propsPerEntity, owner.FullTypeName, new GeneratedRelationPropertyModel
        {
            Kind = RelationPropertyKind.ReferenceNav,
            Name = navName,
            TypeName = rel.TargetTypeName,
            FullTypeName = rel.TargetTypeFullName,
            IsOptional = !resolved.IsRequired,
            Summary = $"Navigation to {rel.TargetTypeName}."
        });

        AddNavigation(navsPerEntity, owner.FullTypeName, new NavigationMetadataModel
        {
            Name = navName,
            TargetTypeName = rel.TargetTypeFullName,
            TargetFullTypeName = rel.TargetTypeFullName,
            NavigationType = "OneToOne",
            // Not rel.InverseProperty: the member on the principal is generated whether or not this
            // declaration named it, and EntityConfigurationTemplate emits `.WithOne(e => e.{Inverse})`
            // from here. Left null, it emitted a bare `.WithOne()` and EF mapped the generated member
            // by convention — as a second relationship, with a shadow key of its own.
            InverseProperty = principalNavExists ? principalNavName : null,
            ForeignKeyProperty = fkName,
            OnDelete = resolved.OnDelete,
            IsRequired = resolved.IsRequired,
            IsPrincipal = false,
            TargetBoundaryTypeFullName = rel.TargetBoundaryTypeFullName,
            IsFromRelationAttribute = true
        });

        // Target (principal) gets: ref nav (if same boundary and not already handled by its own attribute)
        if (targetEntity is not null && !isCrossBoundary)
        {
            AddGeneratedProperty(propsPerEntity, targetEntity.FullTypeName, new GeneratedRelationPropertyModel
            {
                Kind = RelationPropertyKind.ReferenceNav,
                Name = principalNavName,
                TypeName = owner.TypeName,
                FullTypeName = owner.FullTypeName,
                Summary = $"Navigation to dependent {owner.TypeName}."
            });

            AddNavigation(navsPerEntity, targetEntity.FullTypeName, new NavigationMetadataModel
            {
                Name = principalNavName,
                TargetTypeName = owner.FullTypeName,
                TargetFullTypeName = owner.FullTypeName,
                NavigationType = "OneToOne",
                InverseProperty = navName,
                ForeignKeyProperty = fkName,
                IsPrincipal = true,
                TargetBoundaryTypeFullName = owner.BoundaryTypeFullName,
                IsFromRelationAttribute = true
            });
        }
    }
}
