using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Generates repositories, query filters, and persistence metadata.
///     Uses per-entity emission for repositories and query filters,
///     aggregate emission for FilterMapRegistry and metadata.
/// </summary>
internal static class RepositoryFeature
{
    /// <summary>
    ///     Returns the Persistence metadata this compilation generates for itself, so a host that
    ///     declares its own <c>[Entity]</c> types gets their repositories registered.
    /// </summary>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(EntityMetadataModel Entity, DetectedFeatures Features)> perEntityWithFeatures,
        IncrementalValueProvider<(ImmutableArray<EntityMetadataModel> Entities, DetectedFeatures Features)> entitiesWithFeatures,
        IncrementalValueProvider<(ImmutableArray<EntityMetadataModel> Entities, DetectedFeatures Features)> allEntitiesWithFeatures,
        IncrementalValueProvider<bool> isDebugProvider,
        IncrementalValueProvider<ImmutableArray<Traits.Models.TraitEntityInfo>>? localTraitEntities)
    {
        // Per-entity: Repository + query filters for each entity
        // Only regenerated when the specific entity changes
        context.RegisterSourceOutputSafe(perEntityWithFeatures, static (ctx, pair) =>
        {
            if (!PersistenceEngineMarker.HasEngine(pair.Entity, pair.Features))
                return;
            GeneratePerEntityOutputs(
                ctx, pair.Entity, pair.Features.EfCoreProvider, PersistenceEngineMarker.IsEfCore(pair.Entity, pair.Features));
        });

        // Aggregate: FilterMapRegistry + QueryFilterRegistration (need all entities)
        //
        // Trait children travel separately: they are not [Entity] declarations, so they never enter
        // the entity provider. Their ParentVisibilityFilter is generated here and would be registered
        // by nobody without this combine — the leak it closes would stay open on a build that looks
        // like it fixed it.
        // Nullable because a host compilation has no local traits — there the registration is the
        // boundary's own, called through the metadata.
        var traitProvider = localTraitEntities
            ?? context.CompilationProvider.Select(static (_, _) => ImmutableArray<Traits.Models.TraitEntityInfo>.Empty);
        var entitiesWithTraits = entitiesWithFeatures.Combine(traitProvider);
        context.RegisterSourceOutputSafe(entitiesWithTraits, static (ctx, pair) =>
        {
            var ((entities, features), traitEntities) = pair;
            if (!features.HasPersistenceEFCore)
                return;
            GenerateAggregateOutputs(ctx, entities, TraitFilterEntities(traitEntities));
        });

        static ImmutableArray<EntityMetadataModel> TraitFilterEntities(
            ImmutableArray<Traits.Models.TraitEntityInfo> traitEntities)
        {
            if (traitEntities.IsDefaultOrEmpty)
                return ImmutableArray<EntityMetadataModel>.Empty;

            // Only what the registration needs: name, namespace, and the flag. A trait child is not
            // an entity model and must not look like one anywhere else.
            return traitEntities
                .Where(static t => t.HasParentVisibilityFilter || t.HasInternalVisibilityFilter)
                .Select(static t => new EntityMetadataModel
                {
                    TypeName = t.TypeName,
                    FullTypeName = t.FullTypeName,
                    Namespace = t.Namespace,
                    IdType = "System.Guid",
                    Accessibility = "public",
                    IsValid = true,
                    IsFromReference = false,
                    HasParentVisibilityFilter = t.HasParentVisibilityFilter,
                    HasInternalVisibilityFilter = t.HasInternalVisibilityFilter,
                })
                .ToImmutableArray();
        }

        // Aggregate: Persistence metadata (enriched with repository type info for host-side DI). Every entity an engine of this
        // compilation persists, not EF Core's alone: the host reads a module's entities from this document whenever there is one,
        // so an engine boundary's entities left out of it never reached the host (its audit, privacy and permission rules).
        var entitiesWithFeaturesAndDebug = allEntitiesWithFeatures.Combine(isDebugProvider);
        context.RegisterSourceOutputSafe(entitiesWithFeaturesAndDebug, static (ctx, pair) =>
        {
            var ((entities, detectedFeatures), isDebug) = pair;
            if (!detectedFeatures.HasPersistenceEFCore || !detectedFeatures.HasComposition)
                return;
            GeneratePersistenceMetadata(ctx, entities, detectedFeatures, isDebug);
        });

        return entitiesWithFeaturesAndDebug.Select(static (input, _) => LocalPersistenceRegistrations(input));
    }

    /// <summary>
    ///     The same document <see cref="GeneratePersistenceMetadata" /> writes, handed to Composition for
    ///     a host that declares its own entities.
    /// </summary>
    /// <remarks>
    ///     The host renders the <c>IRepository&lt;,&gt;</c> registrations inline from this payload; the
    ///     Persistence metadata declares <c>"registrationMethod": null</c>. Mirrors the generation
    ///     condition exactly, including the <c>!IsFromReference</c> filter — an entity that reached this
    ///     compilation from a reference already describes itself through its own assembly's metadata,
    ///     and contributing it again would register its repository twice.
    /// </remarks>
    private static EquatableArray<Composition.Models.MetadataEntry> LocalPersistenceRegistrations(
        ((ImmutableArray<EntityMetadataModel> Entities, DetectedFeatures Features) Left, bool IsDebug) input)
    {
        var ((entities, detectedFeatures), isDebug) = input;

        if (!detectedFeatures.HasPersistenceEFCore || !detectedFeatures.HasComposition)
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        if (entities.Length == 0)
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        var validEntities = Persisted(entities, detectedFeatures);
        if (validEntities.Length == 0)
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        return ImmutableArray.Create(
            Composition.Models.HostLocalRegistration.CreatePayload(
                Composition.MetadataCategoryIds.Persistence,
                MetadataSchemaVersions.Persistence,
                new PersistenceMetadataTemplate(validEntities, isDebug, e => PersistenceEngineMarker.IsEfCore(e, detectedFeatures)).BuildJson()));
    }

    // =========================================================================
    // Per-entity generation
    // =========================================================================

    /// <summary>
    ///     The entity's repository, EF Core's only, and its filter classes, which are part of its shape.
    /// </summary>
    /// <remarks>
    ///     The filter classes are declared for every engine because callers name them —
    ///     <c>Disable&lt;Reservation.OwnershipFilter&gt;()</c>, <c>[WithoutFilter&lt;T&gt;]</c> — and those names have to
    ///     be the same types whichever engine stores the entity. The temporal query extensions over
    ///     <c>IQueryable</c>, its validation and its virtual navigations are EF Core's.
    /// </remarks>
    private static void GeneratePerEntityOutputs(
        SourceProductionContext context,
        EntityMetadataModel entity,
        EfCoreProvider provider,
        bool isEfCore)
    {
        if (!entity.IsValid || entity.IsFromReference)
            return;

        // Repository
        if (isEfCore)
        {
            var repoTemplate = new RepositoryTemplate(entity, provider);
            var repoArtifact = repoTemplate.RenderOutput();
            if (!repoArtifact.IsEmpty)
                context.AddSource(repoArtifact);
        }

        // The filter classes and the history scope: the list the Sql engine binds against too.
        foreach (var filter in EntityShapeArtifacts.Filters(entity))
        {
            if (!filter.IsEmpty)
                context.AddSource(filter);
        }

        if (entity.IsTemporalRelation)
        {
            if (!isEfCore)
                return;

            var temporalTemplate = new TemporalQueryExtensionsTemplate(entity);
            var artifact = temporalTemplate.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);

            var validationTemplate = new TemporalValidationTemplate(entity);
            var valArtifact = validationTemplate.RenderOutput();
            if (!valArtifact.IsEmpty)
                context.AddSource(valArtifact);

            if (entity.IsTypedTemporalRelation)
            {
                var navTemplate = new TemporalVirtualNavigationTemplate(entity);
                var navArtifact = navTemplate.RenderOutput();
                if (!navArtifact.IsEmpty)
                    context.AddSource(navArtifact);
            }
        }
    }

    // =========================================================================
    // Aggregate generation (needs all entities)
    // =========================================================================

    private static void GenerateAggregateOutputs(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities,
        ImmutableArray<EntityMetadataModel> traitFilterEntities = default)
    {
        if (entities.Length == 0)
            return;

        // Only where the entities are declared. Emitted from every entity, including those read from
        // references, the host produced a second QueryFilterRegistrationExtensions and a second
        // FilterMapRegistry with the same fully-qualified names as the boundary's — CS0436 on every
        // build and CS0121 on the extension call, inside generated code the consumer cannot silence.
        //
        // Skipping them in the host is only correct because the host now calls each boundary's own
        // registration, declared on the Persistence metadata. An earlier attempt to filter here, made
        // before that call existed, compiled and turned three row-level E2E tests red: the filters
        // were generated in the boundary and registered by nobody.
        var declaredHere = entities.Where(e => !e.IsFromReference).ToImmutableArray();
        if (declaredHere.Length == 0)
            return;

        // Trait children join the registration and nothing else: they are not [Entity] declarations
        // and the repository, config and FilterMapRegistry templates must not see them — passing them
        // through the whole aggregate produced 68 errors, one per member a real entity would have.
        var forRegistration = traitFilterEntities.IsDefaultOrEmpty
            ? declaredHere
            : declaredHere.AddRange(traitFilterEntities);

        var registrationTemplate = new QueryFilterRegistrationTemplate(forRegistration);
        var regArtifact = registrationTemplate.RenderOutput();
        if (!regArtifact.IsEmpty)
            context.AddSource(regArtifact);

        var registryTemplate = new FilterMapRegistryTemplate(declaredHere);
        var registryArtifact = registryTemplate.RenderOutput();
        if (!registryArtifact.IsEmpty)
            context.AddSource(registryArtifact);

        // Emit the per-assembly AddPragmaticPersistenceRepositories<TDbContext>() helper
        // unconditionally. In host mode the Composition SG emits its own
        // RegisterAllRepositories() inside PragmaticHost.Services.g.cs that the host infra
        // path invokes — the helper here lives in a separate type (Pragmatic.Persistence.Generated.
        // PragmaticPersistenceRegistration) and a separate assembly, so the two coexist without
        // fighting. Library / sample apps call the helper directly.
        var repoRegTemplate = new RepositoryRegistrationTemplate(entities);
        var repoRegArtifact = repoRegTemplate.RenderOutput();
        if (!repoRegArtifact.IsEmpty)
            context.AddSource(repoRegArtifact);
    }

    private static void GeneratePersistenceMetadata(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities,
        DetectedFeatures features,
        bool isDebug)
    {
        if (entities.Length == 0)
            return;

        var validEntities = Persisted(entities, features);
        if (validEntities.Length == 0)
            return;

        var template = new PersistenceMetadataTemplate(validEntities, isDebug, e => PersistenceEngineMarker.IsEfCore(e, features));
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>The module's own entities an engine the compilation references persists: what its metadata describes.</summary>
    private static ImmutableArray<EntityMetadataModel> Persisted(ImmutableArray<EntityMetadataModel> entities, DetectedFeatures features)
        => entities.Where(e => e.IsValid && !e.IsFromReference && PersistenceEngineMarker.HasEngine(e, features)).ToImmutableArray();
}
