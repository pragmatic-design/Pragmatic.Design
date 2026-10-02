using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Persistence feature orchestrator. Builds shared incremental providers
///     and delegates generation to focused sub-features:
///     <see cref="EntityCoreFeature"/>, <see cref="RepositoryFeature"/>,
///     <see cref="QueryFeature"/>, <see cref="ProjectionFeature"/>,
///     <see cref="AdvancedFeature"/>, <see cref="DbContextFeature"/>.
/// </summary>
internal static class PersistenceFeature
{
    /// <summary>
    ///     Returns the collected entity models for consumption by ManifestFeature, and the Persistence
    ///     metadata this compilation generates for itself so a host that declares its own entities gets
    ///     their repositories registered.
    /// </summary>
    /// <remarks>
    ///     <c>localTraitEntities</c> carries the trait child entities <see cref="Traits.TraitFeature" />
    ///     generates into this compilation. They cannot arrive through the referenced-assembly reader:
    ///     the types do not exist in the compilation being analysed, so nothing can resolve them.
    /// </remarks>
    public static (IncrementalValueProvider<ImmutableArray<EntityMetadataModel>> Entities,
        IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> LocalRegistrations,
        // The routes of the queries derived from specifications. They belong to Endpoints, which runs
        // later and cannot see a type this generator has not written yet, so they travel as models.
        IncrementalValueProvider<ImmutableArray<Endpoints.Models.EndpointModel>> DerivedQueryEndpoints,
        // And every query this assembly declares, for the invoker pass: it runs after the permission
        // catalog exists, and the catalog is built from the entities returned above.
        IncrementalValueProvider<ImmutableArray<Models.QueryModel>> QueryModels,
        // The DbContexts, schemas and migration contexts a host gets, for Composition to call only those.
        IncrementalValueProvider<PersistedStoresModel> PersistedStores) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<Models.QueryModel>>? resourceQueries = null,
        IncrementalValueProvider<ImmutableArray<Traits.Models.TraitEntityInfo>>? localTraitEntities = null)
    {
        // ================================================================
        // Shared providers (used by multiple sub-features)
        // ================================================================

        var compilationProvider = context.CompilationProvider;

        // Entities from referenced assemblies (domain modules)
        // Uses enriched JSON metadata when available (schema >= 1.1), falls back to full type scan
        var referencedEntityProvider = compilationProvider
            .Select((compilation, ct) => EntityMetadataReader.ReadFromPersistenceMetadata(compilation, ct))
            // ImmutableArray compares by backing-array reference; this read re-runs on every compilation
            // change. A sequence comparer keeps the value cached when the referenced entities are unchanged,
            // so the downstream merge/fan-out doesn't regenerate everything.
            .WithComparer(ImmutableArraySequenceComparer<EntityMetadataModel>.Instance)
            .WithTrackingName(TrackingNames.PersistenceReferencedEntities);

        // Entities from current compilation (syntax-based)
        var currentEntityProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => EntityTransform.IsPotentialEntityClass(node),
                transform: static (ctx, ct) => EntityTransform.TransformFromSyntax(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect()
            .WithComparer(ImmutableArraySequenceComparer<EntityMetadataModel>.Instance)
            .WithTrackingName(TrackingNames.PersistenceCurrentEntities);

        // Which boundaries this assembly declares, and what each claims with [Owns<T>]. Read from the
        // compilation because the answer is not visible from one entity symbol: a per-entity transform
        // sees the entity and no boundaries at all.
        var ownershipProvider = compilationProvider
            .Select(static (c, ct) => BoundaryOwnershipReader.Read(c, ct))
            .WithTrackingName(TrackingNames.PersistenceBoundaryOwnership);

        // Merge all entities, deduplicate, build relation graph
        var mergedEntitiesProvider = referencedEntityProvider
            .Combine(currentEntityProvider)
            .Select(static (combined, _) => MergeEntities(combined.Left, combined.Right))
            .WithComparer(ImmutableArraySequenceComparer<EntityMetadataModel>.Instance);

        // An entity never names its boundary: the boundary claims it, or — with one boundary in the
        // assembly — owns everything. Resolved here, before any sub-feature reads BoundaryName, so all
        // of them see the same answer.
        var resolvedProvider = mergedEntitiesProvider
            .Combine(ownershipProvider)
            .Select(static (pair, _) => EntityBoundaryResolver.Resolve(pair.Left, pair.Right));

        context.RegisterSourceOutputSafe(
            resolvedProvider.Combine(ownershipProvider), ReportBoundaryOwnership);

        var allEntitiesProvider = resolvedProvider
            .Select(static (r, _) => r.Entities)
            .WithComparer(ImmutableArraySequenceComparer<EntityMetadataModel>.Instance)
            .WithTrackingName(TrackingNames.PersistenceAllEntities);

        // Gate on HasPersistenceEFCore (aggregate — for features that need all entities)
        var entitiesWithFeatures = allEntitiesProvider.Combine(features);

        ReportInstantsWithoutUtcNormalisation(context, entitiesWithFeatures);

        // Per-entity fan-out: change 1 entity → regenerate only that entity's artifacts
        var perEntityProvider = allEntitiesProvider
            .SelectMany(static (entities, _) => entities);
        var perEntityWithFeatures = perEntityProvider.Combine(features);

        // Debug flag for metadata generation
        var isDebugProvider = compilationProvider
            .Select(static (c, _) => c.Options.OptimizationLevel == OptimizationLevel.Debug);

        // ================================================================
        // Delegate to sub-features
        // ================================================================

        // Per-entity: Relations, Create, Specs, Traits, Diagnostics, Setters
        EntityCoreFeature.Register(context, perEntityWithFeatures, entitiesWithFeatures);

        // Per-entity: Repositories, Query filters; Aggregate: FilterMapRegistry, Metadata
        var localRegistrations =
            RepositoryFeature.Register(context, perEntityWithFeatures, entitiesWithFeatures, isDebugProvider, localTraitEntities);

        var (derivedQueryEndpoints, queryModels) = QueryFeature.Register(context, features, resourceQueries: resourceQueries);
        ProjectionFeature.Register(context, allEntitiesProvider, features);
        AdvancedFeature.Register(context, entitiesWithFeatures, features);
        var persistedStores = DbContextFeature.Register(context, allEntitiesProvider, features, localTraitEntities);

        // TPH/TPT/TPC derived type setters — generates SetX() for properties declared on derived types.
        // The raw Compilation is required: derived types are discovered by resolving each entity SYMBOL
        // (GetTypeByMetadataName) and walking the namespace tree, so this stage re-runs on every edit.
        var derivedSetterProvider = entitiesWithFeatures.Combine(compilationProvider);
        context.RegisterSourceOutputSafe(derivedSetterProvider, static (ctx, pair) =>
        {
            var ((entities, detectedFeatures), compilation) = pair;
            if (!detectedFeatures.HasPersistenceEFCore)
                return;
            GenerateDerivedTypeSetters(ctx, entities, compilation);
        });

        // The {Boundary}Permissions classes are PermissionsClassFeature's: they hold the declared permissions
        // too, which are Identity's to read, so they are registered after it.

        return (allEntitiesProvider, localRegistrations, derivedQueryEndpoints, queryModels, persistedStores);
    }

    // ================================================================
    // TPH derived type setter generation
    // ================================================================

    /// <summary>
    ///     Finds entity base types with [Inheritance], discovers their derived types,
    ///     and generates SetX() methods for properties declared on the derived types.
    /// </summary>
    private static void GenerateDerivedTypeSetters(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities,
        Compilation compilation)
    {
        // Find base entities that have [Inheritance] strategy
        var inheritanceBases = entities
            .Where(e => e.IsValid && !e.IsFromReference && !string.IsNullOrEmpty(e.InheritanceStrategy))
            .ToList();

        if (inheritanceBases.Count == 0)
            return;

        foreach (var baseEntity in inheritanceBases)
        {
            var baseSymbol = compilation.GetTypeByMetadataName(baseEntity.FullTypeName);
            if (baseSymbol is null)
                continue;

            // Find types deriving from this base using targeted lookup
            var derivedTypes = FindDerivedTypes(compilation, entities, baseSymbol);

            foreach (var derivedSymbol in derivedTypes)
            {
                // Collect ONLY properties declared directly on the derived type (not inherited)
                var ownProperties = CollectOwnProperties(derivedSymbol);
                if (ownProperties.IsEmpty)
                    continue;

                var hasPrivateSetters = ownProperties.Any(p => p.HasPrivateSetter);
                if (!hasPrivateSetters)
                    continue;

                // Create a minimal model for setter generation
                var derivedModel = new EntityMetadataModel
                {
                    TypeName = derivedSymbol.Name,
                    FullTypeName = derivedSymbol.ToDisplayString(),
                    Namespace = derivedSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                    IdType = baseEntity.IdType,
                    Accessibility = derivedSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                    IsValid = true,
                    IsAbstract = derivedSymbol.IsAbstract,
                    IsFromReference = false,
                    Properties = ownProperties
                };

                var template = new DerivedEntitySettersTemplate(derivedModel);
                var artifact = template.RenderOutput();
                if (!artifact.IsEmpty)
                    context.AddSource(artifact);
            }
        }
    }

    /// <summary>
    ///     Finds the derived types of an <c>[Inheritance]</c> base whose setters THIS stage owns:
    ///     the ones declared as a plain partial class, discovered only through the base.
    /// </summary>
    /// <remarks>
    ///     A derived type that carries <c>[Entity]</c> is deliberately excluded. It flows through the
    ///     per-entity pipeline, where <c>EntityCoreFeature.GenerateEntitySetters</c> renders the same
    ///     lightweight template for it. Emitting it here as well produced
    ///     <c>{Namespace}.{Type}.Setters.g.cs</c> twice, and Roslyn answers a duplicate hint by throwing
    ///     out every file this generator produced, under a CS8785 that is only a warning — a build that
    ///     goes green with all generated code missing.
    /// </remarks>
    private static List<INamedTypeSymbol> FindDerivedTypes(
        Compilation compilation,
        ImmutableArray<EntityMetadataModel> entities,
        INamedTypeSymbol baseType)
    {
        var declaredEntities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities)
            if (entity.IsValid && !entity.IsFromReference)
                declaredEntities.Add(entity.FullTypeName);

        var scanned = new List<INamedTypeSymbol>();
        ScanForDerived(compilation.SourceModule.GlobalNamespace, baseType, scanned);

        var result = new List<INamedTypeSymbol>();
        foreach (var symbol in scanned)
            if (!declaredEntities.Contains(symbol.ToDisplayString()))
                result.Add(symbol);

        return result;
    }

    private static void ScanForDerived(
        INamespaceSymbol ns, INamedTypeSymbol baseType, List<INamedTypeSymbol> result)
    {
        foreach (var member in ns.GetTypeMembers())
        {
            if (member.TypeKind == TypeKind.Class &&
                !member.IsAbstract &&
                !SymbolEqualityComparer.Default.Equals(member, baseType) &&
                DerivesFrom(member, baseType))
            {
                result.Add(member);
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
            ScanForDerived(childNs, baseType, result);
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    ///     Collects properties declared directly on a derived type (not inherited from base).
    /// </summary>
    private static ImmutableArray<PropertyMetadataModel> CollectOwnProperties(INamedTypeSymbol derivedType)
    {
        var builder = ImmutableArray.CreateBuilder<PropertyMetadataModel>();

        foreach (var member in derivedType.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;

            // Only properties declared on THIS type, not inherited
            if (!SymbolEqualityComparer.Default.Equals(prop.ContainingType, derivedType))
                continue;

            if (prop.IsStatic || prop.IsIndexer)
                continue;

            // Skip read-only properties
            if (prop.SetMethod is null)
                continue;

            var isPrivateSetter = prop.SetMethod.DeclaredAccessibility != Accessibility.Public;
            var typeName = prop.Type.ToDisplayString();

            builder.Add(new PropertyMetadataModel
            {
                Name = prop.Name,
                TypeName = typeName,
                HasPrivateSetter = isPrivateSetter,
                IsNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                             prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
                IsEnum = prop.Type.TypeKind == TypeKind.Enum,
                HasDefaultValue = prop.DeclaringSyntaxReferences.Length > 0
            });
        }

        return builder.ToImmutable();
    }

    // ================================================================
    // Shared helper: merge entities from references + current compilation
    // ================================================================

    private static ImmutableArray<EntityMetadataModel> MergeEntities(
        ImmutableArray<EntityMetadataModel> referenced,
        ImmutableArray<EntityMetadataModel> current)
    {
        var seen = new HashSet<string>();
        var merged = ImmutableArray.CreateBuilder<EntityMetadataModel>();

        var currentEntityFullNames = new HashSet<string>(current.Select(e => e.FullTypeName));

        foreach (var e in referenced)
        {
            if (seen.Add(e.FullTypeName))
            {
                var entity = currentEntityFullNames.Contains(e.FullTypeName)
                    ? e with { IsFromReference = false }
                    : e;
                merged.Add(entity);
            }
        }

        foreach (var e in current)
        {
            if (seen.Add(e.FullTypeName))
                merged.Add(e with { IsFromReference = false });
        }

        // The graph first: a class-level [LogicKey] part that names a generated foreign key takes its
        // type from what the graph put on the entity.
        return LogicKeyPartResolver.Complete(RelationGraphBuilder.BuildRelationGraph(merged.ToImmutable()));
    }

    /// <summary>
    ///     Reports what the ownership resolution could not decide: an entity no boundary claims, one
    ///     that two claim, and an assembly declaring more than one module.
    /// </summary>
    /// <remarks>
    ///     All three are errors rather than warnings because each one ends in an absence rather than a
    ///     failure: an entity outside every boundary reaches no DbContext, so no migration creates its
    ///     table and the first write is what says so.
    /// </remarks>
    private static void ReportBoundaryOwnership(
        SourceProductionContext context,
        ((ImmutableArray<EntityMetadataModel> Entities, ImmutableArray<BoundaryOwnershipProblem> Problems) Resolved,
            BoundaryOwnership Ownership) input)
    {
        var (resolved, ownership) = input;

        if (ownership.ModuleCount > 1)
        {
            var location = ownership.Boundaries.AsImmutableArray()
                .Select(b => b.LocationInfo)
                .FirstOrDefault(l => l is not null);

            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.MultipleModulesInOneAssembly,
                location?.ToLocation(),
                "this assembly",
                ownership.ModuleCount.ToString()));
        }

        foreach (var problem in resolved.Problems)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EntityBoundaryResolver.DescriptorFor(problem.Kind),
                problem.LocationInfo?.ToLocation(),
                problem.EntityName,
                problem.First ?? "",
                problem.Second ?? ""));
        }
    }

    /// <summary>
    ///     Warns the host that stores instants without the convention that normalises them to UTC.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Host mode only, and that is the whole point of putting it here. Whether the
    ///         normalisation is wired is a property of the host — the module that declares the entity
    ///         cannot see it, so the same check there would be false for every solution whose host
    ///         references the package. This is the shape chapter 9 describes: the module declares, the
    ///         host composes, and only the host can answer a question about the whole.
    ///     </para>
    ///     <para>
    ///         One diagnostic per offending property, not one per host: the message names the entity and
    ///         the property, which is what makes it actionable, and a host with none of them stays
    ///         silent.
    ///     </para>
    /// </remarks>
    private static void ReportInstantsWithoutUtcNormalisation(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<(ImmutableArray<EntityMetadataModel> Entities, DetectedFeatures Features)> input)
    {
        context.RegisterSourceOutputSafe(input, static (ctx, pair) =>
        {
            var (entities, features) = pair;

            if (!features.IsHostMode || !features.HasPersistenceEFCore || features.HasTemporalEfCore)
                return;

            var properties = 0;
            var entityCount = 0;
            string? example = null;

            foreach (var entity in entities)
            {
                if (!entity.IsValid)
                    continue;

                var here = 0;
                foreach (var property in entity.Properties)
                {
                    if (!IsInstant(property.TypeName))
                        continue;

                    here++;
                    example ??= $"{entity.TypeName}.{property.Name}";
                }

                if (here == 0)
                    continue;

                properties += here;
                entityCount++;
            }

            if (example is null)
                return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.InstantsAreNotNormalisedToUtc,
                Location.None,
                properties,
                entityCount,
                example));
        });
    }

    /// <summary>Whether a property type is one of the two BCL instants, nullable or not.</summary>
    private static bool IsInstant(string typeName)
    {
        var bare = typeName.TrimEnd('?');
        var simple = bare.Substring(bare.LastIndexOf('.') + 1);

        return simple is "DateTime" or "DateTimeOffset";
    }
}
