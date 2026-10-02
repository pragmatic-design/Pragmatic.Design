using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Builds the cross-entity relationship graph from [Relation.*] attributes.
///     For each entity, computes:
///     1. NavigationMetadataModel entries (for EntityConfiguration)
///     2. GeneratedRelationPropertyModel entries (for EntityRelationsTemplate)
///     Handles the "other side" wiring: e.g., OneToMany on Parent generates FK + ref nav on Child.
/// </summary>
internal static partial class RelationGraphBuilder
{
    /// <summary>
    ///     Processes all entities and builds the full relation graph.
    ///     Returns updated entity models with Navigations and GeneratedRelationProperties populated.
    /// </summary>
    public static ImmutableArray<EntityMetadataModel> BuildRelationGraph(
        ImmutableArray<EntityMetadataModel> entities)
    {
        // Build lookup by full type name and by short type name
        var entityByFullName = new Dictionary<string, EntityMetadataModel>();
        var entityByName = new Dictionary<string, EntityMetadataModel>();
        foreach (var e in entities)
        {
            if (!e.IsValid)
                continue;
            entityByFullName[e.FullTypeName] = e;
            // Short name lookup — first wins (in case of collisions, use full name)
            if (!entityByName.ContainsKey(e.TypeName))
                entityByName[e.TypeName] = e;
        }

        // Accumulate navigations and generated properties per entity
        var navigationsPerEntity = new Dictionary<string, List<NavigationMetadataModel>>();
        var generatedPropsPerEntity = new Dictionary<string, List<GeneratedRelationPropertyModel>>();

        foreach (var entity in entities)
        {
            if (!entity.IsValid || !entity.UsesRelationAttributes)
                continue;

            foreach (var rel in entity.RelationAttributes)
            {
                ProcessRelation(entity, rel, entityByFullName, entityByName,
                    navigationsPerEntity, generatedPropsPerEntity);
            }
        }

        // Rebuild entity models with computed navigations and generated properties.
        // Entities may receive generated properties from OTHER entities' declarations
        // (e.g., a child entity receives FK+nav from parent's [Relation.OneToMany]).
        var result = ImmutableArray.CreateBuilder<EntityMetadataModel>(entities.Length);
        foreach (var entity in entities)
        {
            var hasReceivedNavs = navigationsPerEntity.ContainsKey(entity.FullTypeName);
            var hasReceivedProps = generatedPropsPerEntity.ContainsKey(entity.FullTypeName);

            if (!entity.UsesRelationAttributes && !hasReceivedNavs && !hasReceivedProps)
            {
                // No relation attributes and no generated properties from other entities — keep as-is
                result.Add(entity);
                continue;
            }

            // Merge navigations: relation-generated navs take precedence over heuristic ones
            var navs = MergeNavigations(entity.Navigations.AsImmutableArray(), navigationsPerEntity, entity.FullTypeName);

            // Filter generated properties: exclude any that already exist in source code.
            // Uses AllSourceMemberNames which includes ALL property names (including those filtered
            // as navigations by CollectProperties), preventing CS0102 duplicate definitions.
            var sourcePropertyNames = new HashSet<string>(entity.AllSourceMemberNames);

            // ⚠️ And the members ANOTHER generator will add. The check above looked only at source,
            // so a navigation named like a trait member — [Auditable] plus
            // [Relation.ManyToOne<Person>.WithNavigation("CreatedBy")] — put the same name in two
            // partial parts of one class: CS0102, in files the author cannot open. One generator
            // cannot see another's output, but it can ask the same predictor the rest of the
            // codebase asks.

            foreach (var reserved in TraitMemberNames(entity))
                sourcePropertyNames.Add(reserved);

            var genProps = generatedPropsPerEntity.TryGetValue(entity.FullTypeName, out var propList)
                ? propList.Where(gp => !sourcePropertyNames.Contains(gp.Name)).ToImmutableArray()
                : entity.GeneratedRelationProperties;

            result.Add(entity with
            {
                Navigations = navs,
                GeneratedRelationProperties = genProps
            });
        }

        return result.ToImmutable();
    }

    /// <summary>
    ///     The members the traits template will add to this entity.
    /// </summary>
    /// <remarks>
    ///     Derived from the same flags that template reads, so the two stay in step: a name is
    ///     reserved exactly when the member is going to be emitted. The audit and soft-delete sets
    ///     are all-or-nothing there — <c>HasManual*Props</c> is true only when every one of them is
    ///     already in source — so they are reserved as a group.
    /// </remarks>
    private static IEnumerable<string> TraitMemberNames(EntityMetadataModel entity)
    {
        if (!entity.HasManualPersistenceId)
        {
            yield return "PersistenceId";
            yield return "Id";
        }

        if (entity is { IsAuditable: true, HasManualAuditableProps: false })
        {
            yield return "CreatedAt";
            yield return "CreatedBy";
            yield return "UpdatedAt";
            yield return "UpdatedBy";
        }

        if (entity is { IsSoftDelete: true, HasManualSoftDeleteProps: false })
        {
            yield return "IsDeleted";
            yield return "DeletedAt";
            yield return "DeletedBy";
        }

        if (entity is { IsOwnedEntity: true, HasManualOwnedEntityProps: false })
            yield return "OwnerId";
    }

    private static void ProcessRelation(
        EntityMetadataModel ownerEntity,
        RelationAttributeModel rel,
        Dictionary<string, EntityMetadataModel> entityByFullName,
        Dictionary<string, EntityMetadataModel> entityByName,
        Dictionary<string, List<NavigationMetadataModel>> navigationsPerEntity,
        Dictionary<string, List<GeneratedRelationPropertyModel>> generatedPropsPerEntity)
    {
        var targetEntity = FindEntity(rel.TargetTypeFullName, rel.TargetTypeName, entityByFullName, entityByName);
        // A target the owner's boundary reads with [ReadAccess<T>] sits in the owner's DbContext, so
        // the navigation is generated: degrading it to the key alone is what forced the reference
        // application to write the navigation by hand across Booking → Catalog.
        var isCrossBoundary = IsCrossBoundary(ownerEntity.BoundaryTypeFullName, rel.TargetBoundaryTypeFullName)
                              && !ownerEntity.ReadAccessTypes.AsImmutableArray().Contains(rel.TargetTypeFullName);

        switch (rel.RelationType)
        {
            case "OneToMany":
                ProcessOneToMany(ownerEntity, rel, targetEntity, isCrossBoundary,
                    navigationsPerEntity, generatedPropsPerEntity);
                break;
            case "ManyToOne":
                ProcessManyToOne(ownerEntity, rel, targetEntity, isCrossBoundary,
                    navigationsPerEntity, generatedPropsPerEntity);
                break;
            case "ManyToMany":
                ProcessManyToMany(ownerEntity, rel, targetEntity, isCrossBoundary,
                    navigationsPerEntity, generatedPropsPerEntity);
                break;
            case "OneToOne":
                ProcessOneToOne(ownerEntity, rel, targetEntity, isCrossBoundary,
                    navigationsPerEntity, generatedPropsPerEntity);
                break;
        }
    }

    /// <summary>
    ///     Merges heuristic navigations with relation-generated navigations.
    ///     Relation-generated navs take precedence for name conflicts (have richer metadata).
    /// </summary>
    private static ImmutableArray<NavigationMetadataModel> MergeNavigations(
        ImmutableArray<NavigationMetadataModel> heuristicNavs,
        Dictionary<string, List<NavigationMetadataModel>> graphNavs,
        string entityFullName)
    {
        if (!graphNavs.TryGetValue(entityFullName, out var graphNavList))
            return heuristicNavs;

        var merged = new List<NavigationMetadataModel>(heuristicNavs);
        foreach (var graphNav in graphNavList)
        {
            // Remove heuristic nav with same name (graph nav has better metadata)
            merged.RemoveAll(n => n.Name == graphNav.Name);
            merged.Add(graphNav);
        }

        return merged.ToImmutableArray();
    }

    private static EntityMetadataModel? FindEntity(
        string fullName, string shortName,
        Dictionary<string, EntityMetadataModel> byFullName,
        Dictionary<string, EntityMetadataModel> byName)
    {
        if (byFullName.TryGetValue(fullName, out var entity))
            return entity;
        if (byName.TryGetValue(shortName, out entity))
            return entity;
        return null;
    }

    private static bool IsCrossBoundary(string? ownerBoundary, string? targetBoundary)
    {
        if (string.IsNullOrEmpty(ownerBoundary) || string.IsNullOrEmpty(targetBoundary))
            return false;
        return ownerBoundary != targetBoundary;
    }

    private static void AddNavigation(
        Dictionary<string, List<NavigationMetadataModel>> dict,
        string entityFullName,
        NavigationMetadataModel nav)
    {
        if (!dict.TryGetValue(entityFullName, out var list))
        {
            list = new List<NavigationMetadataModel>();
            dict[entityFullName] = list;
        }

        // Avoid duplicates by name
        if (list.All(n => n.Name != nav.Name))
            list.Add(nav);
    }

    private static void AddGeneratedProperty(
        Dictionary<string, List<GeneratedRelationPropertyModel>> dict,
        string entityFullName,
        GeneratedRelationPropertyModel prop)
    {
        if (!dict.TryGetValue(entityFullName, out var list))
        {
            list = new List<GeneratedRelationPropertyModel>();
            dict[entityFullName] = list;
        }

        // Avoid duplicates by name
        if (list.All(p => p.Name != prop.Name))
            list.Add(prop);
    }
}
