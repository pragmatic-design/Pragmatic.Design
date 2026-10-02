using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGenerator.Features.Persistence.Validation;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Generates BoundaryDbContexts, MigrationDbContexts, and DbContext DI registration.
///     Requires host-mode (entities from referenced assemblies) to generate.
/// </summary>
internal static class DbContextFeature
{
    /// <remarks>
    ///     <c>localTraitEntities</c> carries the trait child entities this compilation generates. A host
    ///     that declares <c>[HasComments]</c> itself has no other way to get them into its DbContexts:
    ///     they are created by this generator run, so neither the trait metadata attribute nor the types
    ///     themselves exist yet to be read back.
    /// </remarks>
    /// <returns>
    ///     The stores these outputs write into a host, for Composition to name only those
    ///     (see <see cref="PersistedStoresModel" />).
    /// </returns>
    public static IncrementalValueProvider<PersistedStoresModel> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<EntityMetadataModel>> allEntitiesProvider,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<Traits.Models.TraitEntityInfo>>? localTraitEntities = null)
    {
        var compilationProvider = context.CompilationProvider;

        // The host artifacts are named after the assembly that generates them, not after an entity.
        // A string, so combining it costs the incremental pipeline nothing.
        var assemblyNameProvider = compilationProvider.Select(static (c, _) => c.AssemblyName ?? "");

        // Read ReadAccess types per boundary
        var readAccessProvider = compilationProvider
            .Select((compilation, ct) =>
                EntityMetadataReader.ReadReadAccessTypesByBoundary(compilation, ct))
            .WithTrackingName(TrackingNames.PersistenceReadAccess);

        // Detect i18n EFCore reference
        var i18nEFCoreProvider = compilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName(
                    "Pragmatic.Internationalization.EntityFrameworkCore.Extensions.ModelBuilderExtensions") is not null);

        // Read database topology
        var topologyProvider = compilationProvider
            .Select(static (compilation, ct) => DatabaseTopologyReader.Read(compilation, ct))
            .WithTrackingName(TrackingNames.PersistenceDatabaseTopology);

        // The four boundary readers below genuinely need the raw Compilation: boundary marker types live
        // in REFERENCED assemblies, so their attributes are only reachable via GetTypeByMetadataName —
        // ForAttributeWithMetadataName sees the current compilation's syntax only. They each project down
        // to an EquatableArray<string>, so everything downstream of them stays value-cached.

        // Boundaries marked [EnableEventOutbox] — read from metadata (boundaries are in referenced assemblies).
        var outboxBoundariesProvider = allEntitiesProvider
            .Combine(compilationProvider)
            .Select(static (pair, ct) => EventOutboxBoundaryReader.ReadEnabledBoundaries(pair.Right, pair.Left, ct));

        // Boundaries marked [EnableSagaPersistence] — same metadata-based read (mirrors the outbox path).
        var sagaBoundariesProvider = allEntitiesProvider
            .Combine(compilationProvider)
            .Select(static (pair, ct) => SagaPersistenceBoundaryReader.ReadEnabledBoundaries(pair.Right, pair.Left, ct));

        // Boundaries marked [EnableOutbox] (Messaging transport-publish outbox) — same metadata-based read.
        var messagingOutboxBoundariesProvider = allEntitiesProvider
            .Combine(compilationProvider)
            .Select(static (pair, ct) => MessagingOutboxBoundaryReader.ReadEnabledBoundaries(pair.Right, pair.Left, ct));

        // Boundary marked [EnableBatchProgress] (single-owner batch progress table) — same metadata-based read.
        var batchBoundariesProvider = allEntitiesProvider
            .Combine(compilationProvider)
            .Select(static (pair, ct) => BatchProgressBoundaryReader.ReadEnabledBoundaries(pair.Right, pair.Left, ct));

        // Boundary marked [EnableJobPersistence] (single-owner __Jobs/__RecurringJobs) — same read.
        var jobBoundariesProvider = allEntitiesProvider
            .Combine(compilationProvider)
            .Select(static (pair, ct) => JobPersistenceBoundaryReader.ReadEnabledBoundaries(pair.Right, pair.Left, ct));

        // Boundary marked [StoresNotifications] (__Notifications) — same read. Without it the package's
        // own DbContext would have no schema in a composed host: the generated entry point creates one
        // context per declared database and nothing else, so UseEfCoreStore() would wire a store whose
        // table does not exist and the first send would fail at run time.
        var notificationBoundariesProvider = allEntitiesProvider
            .Combine(compilationProvider)
            .Select(static (pair, ct) => NotificationStoreBoundaryReader.ReadEnabledBoundaries(pair.Right, pair.Left, ct));

        // Boundaries an application can configure — an IBoundary, or about to be one — whose UseDatabase
        // the registration applies. Read from the type for the same reason as the four above.
        var configurableBoundariesProvider = allEntitiesProvider
            .Combine(compilationProvider)
            .Select(static (pair, ct) => ConfigurableBoundaryReader.Read(pair.Right, pair.Left, ct));

        // Combine all inputs
        var dbContextInputProvider = allEntitiesProvider
            .Combine(readAccessProvider)
            .Combine(i18nEFCoreProvider)
            .Combine(topologyProvider)
            .Combine(features);

        // Read trait entities from referenced assemblies for DbContext inclusion
        var traitEntityProvider = compilationProvider
            .Select(static (compilation, ct) => TraitEntityMetadataReader.ReadFromReferencedAssemblies(compilation, ct));

        // …and merge in the ones this compilation generates, for a host that declares its own traits.
        // Deduplicated by namespace-qualified name and ordered by it: the same trait entity can be both
        // generated here and described by a reference, and a duplicate would configure the same EF entity
        // type twice.
        // The comparer is not decoration: the trait models arrive as a raw ImmutableArray, which compares
        // by backing-array reference, so without it this Combine would mark the merge Modified on every
        // keystroke — and this provider feeds every DbContext in the host.
        if (localTraitEntities is not null)
            traitEntityProvider = traitEntityProvider
                .Combine(localTraitEntities.Value.WithComparer(
                    ImmutableArraySequenceComparer<Traits.Models.TraitEntityInfo>.Instance))
                .Select(static (pair, _) => MergeTraitEntities(pair.Left, pair.Right));

        // Tracked at the end, so the name names what actually reaches DbContext generation.
        traitEntityProvider = traitEntityProvider.WithTrackingName(TrackingNames.PersistenceTraitEntities);

        var dbContextWithTraitsProvider = dbContextInputProvider
            .Combine(traitEntityProvider)
            .Combine(outboxBoundariesProvider)
            .Combine(sagaBoundariesProvider)
            .Combine(messagingOutboxBoundariesProvider)
            .Combine(batchBoundariesProvider)
            .Combine(assemblyNameProvider)
            .Combine(jobBoundariesProvider)
            .Combine(notificationBoundariesProvider)
            .WithTrackingName(TrackingNames.PersistenceDbContextInput);

        context.RegisterSourceOutputSafe(dbContextWithTraitsProvider, (ctx, pair) =>
        {
            var ((((((((dbCtxInput, traitEntities), outboxBoundaries), sagaBoundaries), messagingOutboxBoundaries), batchBoundaries), assemblyName), jobBoundaries), notificationBoundaries) = pair;
            if (!dbCtxInput.Right.HasPersistenceEFCore)
                return;
            GenerateBoundaryDbContexts(ctx, dbCtxInput.Left, dbCtxInput.Right.EfCoreProvider,
                dbCtxInput.Right.IsHostMode, assemblyName, traitEntities,
                outboxBoundaries, dbCtxInput.Right.HasEventsEFCore,
                dbCtxInput.Right.HasMessagingEFCore ? sagaBoundaries : EquatableArray<string>.Empty,
                dbCtxInput.Right.HasMessagingEFCore ? messagingOutboxBoundaries : EquatableArray<string>.Empty,
                dbCtxInput.Right.HasMessagingBatch ? batchBoundaries : EquatableArray<string>.Empty,
                dbCtxInput.Right.HasPrivacyEFCore,
                dbCtxInput.Right.HasCryptographyEFCore,
                // The package gates the set, so an [EnableJobPersistence] without Pragmatic.Jobs.EFCore
                // maps nothing rather than naming a type this compilation has not got (PRAG2508).
                dbCtxInput.Right.HasJobsEFCore ? jobBoundaries : EquatableArray<string>.Empty,
                dbCtxInput.Right.HasNotificationsEFCore ? notificationBoundaries : EquatableArray<string>.Empty);
        });

        // PRAG0706 — [ReadAccess] whose target entity is owned by a boundary on another database.
        // Host-only: the topology lives in the host's [Module], so this is the one compilation that can
        // see both sides of the split.
        var readAccessTopologyProvider = allEntitiesProvider
            .Combine(readAccessProvider)
            .Combine(topologyProvider)
            .Combine(features);

        context.RegisterSourceOutputSafe(readAccessTopologyProvider, static (ctx, pair) =>
        {
            var (((entities, readAccess), topology), feats) = pair;
            if (!feats.HasPersistenceEFCore)
                return;
            ReadAccessTopologyValidator.Validate(ctx, entities, readAccess, topology);

            // PRAG0639 — and this one needs no topology: a read entity whose navigation target the
            // reader does not have is ignored in the model whether the databases are split or not.
            ReadAccessNavigationValidator.Validate(ctx, entities, readAccess);
        });

        // DbContext registration extensions (host-mode DI)
        var entitiesWithFeatures = allEntitiesProvider.Combine(features).Combine(outboxBoundariesProvider)
            .Combine(sagaBoundariesProvider).Combine(messagingOutboxBoundariesProvider).Combine(batchBoundariesProvider)
            .Combine(assemblyNameProvider).Combine(configurableBoundariesProvider).Combine(jobBoundariesProvider)
            .Combine(notificationBoundariesProvider);
        context.RegisterSourceOutputSafe(entitiesWithFeatures, (ctx, pair) =>
        {
            var ((((((((entitiesFeat, outboxBoundaries), sagaBoundaries), messagingOutboxBoundaries), batchBoundaries), assemblyName), configurableBoundaries), jobBoundaries), notificationBoundaries) = pair;
            if (!entitiesFeat.Right.HasPersistenceEFCore)
                return;
            GenerateDbContextRegistration(ctx, entitiesFeat.Left, entitiesFeat.Right, assemblyName, outboxBoundaries,
                entitiesFeat.Right.HasMessagingEFCore ? sagaBoundaries : EquatableArray<string>.Empty,
                entitiesFeat.Right.HasMessagingEFCore ? messagingOutboxBoundaries : EquatableArray<string>.Empty,
                entitiesFeat.Right.HasMessagingBatch ? batchBoundaries : EquatableArray<string>.Empty,
                configurableBoundaries,
                entitiesFeat.Right.HasJobsEFCore ? jobBoundaries : EquatableArray<string>.Empty,
                entitiesFeat.Right.HasNotificationsEFCore ? notificationBoundaries : EquatableArray<string>.Empty);
        });

        // Schema metadata generation (opt-in via Pragmatic.Migrations reference).
        // The raw Compilation is required: owned-type columns and TPH derived-type columns are resolved
        // from SYMBOLS (BuildSchemaResolutionContext / ResolveTraitEntityProperties walk the type
        // hierarchy), so no scalar projection preserves the information.
        var schemaMetadataProvider = dbContextInputProvider.Combine(compilationProvider)
            .Combine(traitEntityProvider)
            .Combine(outboxBoundariesProvider)
            .Combine(sagaBoundariesProvider)
            .Combine(messagingOutboxBoundariesProvider)
            .Combine(batchBoundariesProvider)
            .Combine(jobBoundariesProvider)
            .Combine(notificationBoundariesProvider);
        context.RegisterSourceOutputSafe(schemaMetadataProvider, (ctx, pair) =>
        {
            var ((((((((dbCtxInput, compilation), traitEnts), outboxBoundaries), sagaBoundaries), messagingOutboxBoundaries), batchBoundaries), jobBoundaries), notificationBoundaries) = pair;
            var features = dbCtxInput.Right;
            if (!features.HasPersistenceEFCore || !features.HasMigrations)
                return;

            // Inject trait entities as minimal EntityMetadataModel into the entity list
            var input = dbCtxInput.Left;
            if (!traitEnts.IsDefaultOrEmpty)
            {
                var enrichedEntities = input.Left.Left.Left.ToBuilder();
                foreach (var traitEntity in traitEnts)
                {
                    var props = ResolveTraitEntityProperties(compilation, traitEntity);
                    // Resolve BoundaryTypeFullName from entities that share the same BoundaryName
                    var boundaryFqn = input.Left.Left.Left
                        .FirstOrDefault(e => e.BoundaryName == traitEntity.BoundaryName)?.BoundaryTypeFullName;

                    enrichedEntities.Add(new EntityMetadataModel
                    {
                        TypeName = traitEntity.TypeName,
                        FullTypeName = traitEntity.FullTypeName,
                        Namespace = traitEntity.Namespace,
                        IdType = "System.Guid",
                        Accessibility = "public",
                        IsValid = true,
                        IsFromReference = true,
                        BoundaryName = traitEntity.BoundaryName,
                        BoundaryTypeFullName = boundaryFqn,
                        Properties = props,
                        // Per trait: the shared tag and the junction have no soft-delete columns, so
                        // claiming otherwise produced three columns no POCO maps plus an index on them.
                        IsSoftDelete = traitEntity.IsSoftDelete,
                        HasParentVisibilityFilter = traitEntity.HasParentVisibilityFilter,
                        HasInternalVisibilityFilter = traitEntity.HasInternalVisibilityFilter,
                        ParentTenantNavigation = traitEntity.ParentTenantNavigation,
                        KeyColumns = ParseKeyColumns(traitEntity.KeyColumns),
                        UniqueColumns = ParseKeyColumns(traitEntity.UniqueColumns),
                        // Foreign keys and their indexes: BuildIndexes/BuildForeignKeys read them from
                        // here, so without this the trait tables reached the database with no referential
                        // integrity and no index — every read a sequential scan.
                        Navigations = BuildTraitNavigations(traitEntity, boundaryFqn),
                    });
                }

                input = (((enrichedEntities.ToImmutable(), input.Left.Left.Right), input.Left.Right), input.Right);
            }

            var schemaContext = BuildSchemaResolutionContext(compilation, input);
            GenerateSchemaMetadata(ctx, input, features.EfCoreProvider, schemaContext, features.IsHostMode,
                compilation.AssemblyName,
                features.HasEventsEFCore ? outboxBoundaries : EquatableArray<string>.Empty,
                features.HasMessagingEFCore ? sagaBoundaries : EquatableArray<string>.Empty,
                features.HasMessagingEFCore ? messagingOutboxBoundaries : EquatableArray<string>.Empty,
                features.HasMessagingBatch ? batchBoundaries : EquatableArray<string>.Empty,
                features.HasPrivacyEFCore,
                features.HasCryptographyEFCore,
                features.HasJobsEFCore ? jobBoundaries : EquatableArray<string>.Empty,
                features.HasNotificationsEFCore ? notificationBoundaries : EquatableArray<string>.Empty);
        });

        // Diagnostic (fires in the boundary library where [EnableEventOutbox] is authored):
        // the attribute is a no-op unless the project references Pragmatic.Events.EFCore.
        var enableOutboxSites = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.EnableEventOutbox,
                predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                transform: static (attrCtx, _) =>
                    (Name: attrCtx.TargetSymbol.Name, Location: LocationInfo.From(attrCtx.TargetNode.GetLocation())))
            .Combine(features);

        context.RegisterSourceOutputSafe(enableOutboxSites, static (ctx, pair) =>
        {
            var (site, feats) = pair;
            if (feats.HasEventsEFCore)
                return;
            ctx.ReportDiagnostic(Diagnostic.Create(
                Lifecycle.Diagnostics.LifecycleEventsDiagnostics.EnableEventOutboxWithoutEFCore,
                site.Location?.ToLocation() ?? Location.None,
                site.Name));
        });

        // Diagnostic (fires in the boundary library where [EnableSagaPersistence] is authored):
        // the attribute is a no-op unless the project references Pragmatic.Messaging.EFCore.
        var enableSagaSites = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.EnableSagaPersistence,
                predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                transform: static (attrCtx, _) =>
                    (Name: attrCtx.TargetSymbol.Name, Location: LocationInfo.From(attrCtx.TargetNode.GetLocation())))
            .Combine(features);

        context.RegisterSourceOutputSafe(enableSagaSites, static (ctx, pair) =>
        {
            var (site, feats) = pair;
            if (feats.HasMessagingEFCore)
                return;
            ctx.ReportDiagnostic(Diagnostic.Create(
                Messaging.Diagnostics.MessagingDiagnostics.EnableSagaPersistenceWithoutEFCore,
                site.Location?.ToLocation() ?? Location.None,
                site.Name));
        });

        // Diagnostics (fire in the boundary library where [EnableOutbox] is authored):
        // - PRAG0831: the Messaging transport-publish outbox is a no-op unless the project references
        //   Pragmatic.Messaging.EFCore (mirrors the saga path above);
        // - PRAG0833: the same boundary also carries [EnableEventOutbox], so both capture the same events.
        var enableMessagingOutboxSites = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.EnableOutbox,
                predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                transform: static (attrCtx, _) =>
                    (Name: attrCtx.TargetSymbol.Name,
                     Location: LocationInfo.From(attrCtx.TargetNode.GetLocation()),
                     HasEventOutbox: attrCtx.TargetSymbol.GetAttributes().Any(a =>
                         a.AttributeClass is { Name: "EnableEventOutboxAttribute" } cls
                         && cls.ContainingNamespace?.ToDisplayString() == "Pragmatic.Events.Attributes")))
            .Combine(features);

        context.RegisterSourceOutputSafe(enableMessagingOutboxSites, static (ctx, pair) =>
        {
            var (site, feats) = pair;
            if (!feats.HasMessagingEFCore)
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Messaging.Diagnostics.MessagingDiagnostics.EnableOutboxWithoutEFCore,
                    site.Location?.ToLocation() ?? Location.None,
                    site.Name));

            if (site.HasEventOutbox)
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Messaging.Diagnostics.MessagingDiagnostics.ConflictingOutboxAttributes,
                    site.Location?.ToLocation() ?? Location.None,
                    site.Name));
        });

        // Diagnostics for [EnableBatchProgress] (single-owner batch progress table):
        // - PRAG0835: no-op unless the project references Pragmatic.Messaging.Batch;
        // - PRAG0834: more than one boundary carries it (batch progress is a single store).
        var enableBatchSites = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.EnableBatchProgress,
                predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                transform: static (attrCtx, _) =>
                    (Name: attrCtx.TargetSymbol.Name, Location: LocationInfo.From(attrCtx.TargetNode.GetLocation())))
            .Collect()
            .Combine(features);

        context.RegisterSourceOutputSafe(enableBatchSites, static (ctx, pair) =>
        {
            var (sites, feats) = pair;
            foreach (var site in sites)
            {
                if (!feats.HasMessagingBatch)
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Messaging.Diagnostics.MessagingDiagnostics.EnableBatchProgressWithoutBatch,
                        site.Location?.ToLocation() ?? Location.None,
                        site.Name));

                if (sites.Length > 1)
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Messaging.Diagnostics.MessagingDiagnostics.MultipleBatchProgressBoundaries,
                        site.Location?.ToLocation() ?? Location.None,
                        site.Name));
            }
        });

        // Diagnostic (fires in the boundary library where [StoresNotifications] is authored): the
        // attribute is a no-op unless the project references Pragmatic.Notifications.EFCore.
        var storesNotificationsSites = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.StoresNotifications,
                predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                transform: static (attrCtx, _) =>
                    (Name: attrCtx.TargetSymbol.Name, Location: LocationInfo.From(attrCtx.TargetNode.GetLocation())))
            .Combine(features);

        context.RegisterSourceOutputSafe(storesNotificationsSites, static (ctx, pair) =>
        {
            var (site, feats) = pair;
            if (feats.HasNotificationsEFCore)
                return;
            ctx.ReportDiagnostic(Diagnostic.Create(
                Notifications.Diagnostics.NotificationsDiagnostics.StoresNotificationsWithoutEFCore,
                site.Location?.ToLocation() ?? Location.None,
                site.Name));
        });

        // Diagnostics for [EnableJobPersistence] (single-owner __Jobs/__RecurringJobs), the same pair:
        // - PRAG2508: no-op unless the project references Pragmatic.Jobs.EFCore;
        // - PRAG2509: more than one boundary carries it (the job store is a single store).
        var enableJobPersistenceSites = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.EnableJobPersistence,
                predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                transform: static (attrCtx, _) =>
                    (Name: attrCtx.TargetSymbol.Name, Location: LocationInfo.From(attrCtx.TargetNode.GetLocation())))
            .Collect()
            .Combine(features);

        context.RegisterSourceOutputSafe(enableJobPersistenceSites, static (ctx, pair) =>
        {
            var (sites, feats) = pair;
            foreach (var site in sites)
            {
                if (!feats.HasJobsEFCore)
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Jobs.Diagnostics.JobsDiagnostics.JobPersistenceWithoutEfCore,
                        site.Location?.ToLocation() ?? Location.None,
                        site.Name));

                if (sites.Length > 1)
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Jobs.Diagnostics.JobsDiagnostics.JobPersistenceOnMoreThanOneBoundary,
                        site.Location?.ToLocation() ?? Location.None,
                        site.Name));
            }
        });

        // Gated like the emitters above: no DbContext, schema or migration context outside a host that
        // references Persistence.EFCore.
        // The registration class is named by the same rule GenerateDbContextRegistration declares it with,
        // over the same entities and assembly name: the two cannot drift apart.
        return allEntitiesProvider
            .Combine(topologyProvider)
            .Combine(features)
            .Combine(assemblyNameProvider)
            .Select(static (pair, _) =>
            {
                var (((entities, topology), feats), assemblyName) = pair;
                return feats.HasPersistenceEFCore && feats.IsHostMode
                    ? PersistedStoresModel.From(entities, topology,
                        DbContextRegistrationTemplate.QualifiedClassFor(HostArtifactNamespace(assemblyName, entities)))
                    : PersistedStoresModel.None;
            });
    }

    /// <summary>
    ///     Union of the trait entities read from references and those generated here, keyed by
    ///     <c>{Namespace}.{TypeName}</c> and ordered by it so the generated DbContexts are stable.
    /// </summary>
    private static EquatableArray<Traits.Models.TraitEntityInfo> MergeTraitEntities(
        EquatableArray<Traits.Models.TraitEntityInfo> fromReferences,
        ImmutableArray<Traits.Models.TraitEntityInfo> fromCompilation)
    {
        if (fromCompilation.IsDefaultOrEmpty)
            return fromReferences;

        static string Key(Traits.Models.TraitEntityInfo info) => info.Namespace + "." + info.TypeName;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = ImmutableArray.CreateBuilder<Traits.Models.TraitEntityInfo>();

        foreach (var info in fromReferences.AsImmutableArray())
            if (seen.Add(Key(info)))
                merged.Add(info);

        foreach (var info in fromCompilation)
            if (seen.Add(Key(info)))
                merged.Add(info);

        return new EquatableArray<Traits.Models.TraitEntityInfo>(
            merged.OrderBy(Key, StringComparer.Ordinal).ToImmutableArray());
    }

    // =========================================================================
    // Generate methods
    // =========================================================================

    /// <remarks>
    ///     <para>
    ///         <paramref name="isHostMode" /> is what decides whether anything is emitted. It is not
    ///         <c>entities.Any(e =&gt; e.IsFromReference)</c>: the two usually coincide — a boundary
    ///         library sees only its own entities, a host sees only its references' — but not for a
    ///         host that declares an entity itself, which would get no DbContext, no entity
    ///         configuration and no migration context at all.
    ///     </para>
    ///     <para>
    ///         Only the host knows the provider and the topology, so "host mode" is the condition that
    ///         matters; a library must emit nothing, and with this gate it does.
    ///     </para>
    /// </remarks>
    private static void GenerateBoundaryDbContexts(
        SourceProductionContext context,
        (((ImmutableArray<EntityMetadataModel> Entities,
            EquatableDictionary<string, EquatableArray<string>> ReadAccessByBoundary) Input,
            bool HasI18nEFCore),
            DatabaseTopologyInfo Topology) input,
        EfCoreProvider efCoreProvider,
        bool isHostMode,
        string? assemblyName = null,
        EquatableArray<Traits.Models.TraitEntityInfo> traitEntities = default,
        EquatableArray<string> outboxBoundaries = default,
        bool hasEventsEFCore = false,
        EquatableArray<string> sagaBoundaries = default,
        EquatableArray<string> messagingOutboxBoundaries = default,
        EquatableArray<string> batchBoundaries = default,
        bool hasPrivacyEFCore = false,
        bool hasCryptographyEFCore = false,
        EquatableArray<string> jobBoundaries = default,
        EquatableArray<string> notificationBoundaries = default)
    {
        var entities = input.Item1.Input.Entities;
        var outboxBoundarySet = outboxBoundaries.AsImmutableArray();
        var sagaBoundarySet = sagaBoundaries.AsImmutableArray();
        var messagingOutboxBoundarySet = messagingOutboxBoundaries.AsImmutableArray();
        var batchBoundarySet = batchBoundaries.AsImmutableArray();
        var jobBoundarySet = jobBoundaries.AsImmutableArray();
        var notificationBoundarySet = notificationBoundaries.AsImmutableArray();
        var readAccessByBoundary = input.Item1.Input.ReadAccessByBoundary;
        var hasI18nEFCore = input.Item1.HasI18nEFCore;
        var topology = input.Topology;

        if (entities.Length == 0)
            return;
        if (!isHostMode)
            return;

        // Generate EntityConfigurations at host level (internal sealed).
        // Only the host knows the database provider, so configs are always generated here.
        GenerateEntityConfigurations(context, entities);

        var entityLookup = entities
            .Where(e => e.IsValid)
            .ToDictionary(e => e.FullTypeName, e => e);

        var boundaryGroups = entities
            .Where(e => e.IsValid && !string.IsNullOrEmpty(e.BoundaryName))
            .GroupBy(e => e.BoundaryName!)
            .ToImmutableArray();

        foreach (var group in boundaryGroups)
        {
            var boundaryName = group.Key;
            var directEntities = group.ToImmutableArray();
            var allRequiredEntities = CollectRequiredEntities(directEntities, entityLookup);

            var ns = BoundaryNamespace(directEntities);

            var boundaryTypeName = directEntities
                .Select(e => e.BoundaryTypeFullName)
                .FirstOrDefault(n => !string.IsNullOrEmpty(n));
            if (string.IsNullOrEmpty(boundaryTypeName))
            {
                boundaryTypeName = string.IsNullOrEmpty(ns)
                    ? $"{boundaryName}Boundary"
                    : $"{ns}.{boundaryName}Boundary";
            }

            var readAccessEntities = BuildReadAccessEntities(boundaryTypeName, readAccessByBoundary, entityLookup);
            var boundaryEntityFullNames = new HashSet<string>(directEntities.Select(e => e.FullTypeName));
            var readAccessEntityNames = new HashSet<string>(readAccessEntities.Select(e => e.FullTypeName));
            var crossBoundaryTypes = CollectCrossBoundaryTypes(
                    directEntities, readAccessEntities, entityLookup, boundaryEntityFullNames,
                    readAccessEntityNames)
                .Concat(BorrowedTraitEntities(traitEntities, boundaryName, readAccessEntities))
                .Distinct()
                .ToImmutableArray();

            var inheritanceConfigs = BuildInheritanceConfigs(directEntities);

            // Build entity list including trait-generated entities
            var entityModels = directEntities.Select(e => new DbContextEntityModel
            {
                FullTypeName = e.FullTypeName,
                TypeName = e.TypeName,
                DbSetName = StringHelper.Pluralize(e.TypeName),
                ConfigurationTypeName = $"{e.TypeName}EntityConfig",
                IsAbstract = false,
                IsConcurrencyAware = e.IsConcurrencyAware,
                IsTenantEntity = e.IsTenantEntity,
                IsAudited = e.IsAudited,
                IsDataSubject = e.IsDataSubject,
                LogicKeyPropertyName = e.LogicKey,
                LogicKeySelector = e.LogicKeys.Length > 0 ? e.LogicKeySelector : null,
                IsSoftDelete = e.IsSoftDelete,
                TemporalMaxOneParentKey = TemporalMaxOneParentKey(e)
            }).ToList();

            // Add trait entities belonging to this boundary
            if (!traitEntities.IsDefaultOrEmpty)
            {
                foreach (var traitEntity in traitEntities.Where(t => t.BoundaryName == boundaryName))
                {
                    entityModels.Add(new DbContextEntityModel
                    {
                        FullTypeName = traitEntity.FullTypeName,
                        TypeName = traitEntity.TypeName,
                        DbSetName = StringHelper.Pluralize(traitEntity.TypeName),
                        ConfigurationTypeName = $"{traitEntity.TypeName}EntityConfig",
                        IsAbstract = false,
                        IsConcurrencyAware = false,
                        IsTraitEntity = true,
                        ParentTenantNavigation = traitEntity.ParentTenantNavigation,
                    });
                }
            }

            var model = new BoundaryDbContextModel
            {
                Namespace = ns + ".Entities",
                ClassName = $"{boundaryName}DbContext",
                BoundaryName = boundaryName,
                BoundaryTypeName = boundaryTypeName,
                IsMigrationContext = false,
                CrossBoundaryEntityTypes = crossBoundaryTypes,
                ReadAccessEntities = readAccessEntities,
                InheritanceConfigurationTypes = inheritanceConfigs,
                HasI18nEFCore = hasI18nEFCore,
                HasPrivacyEFCore = hasPrivacyEFCore,
                HasCryptographyEFCore = hasCryptographyEFCore,
                EfCoreProvider = efCoreProvider,
                HasEventOutbox = hasEventsEFCore && !string.IsNullOrEmpty(boundaryTypeName)
                    && outboxBoundarySet.Contains(boundaryTypeName!),
                HasSagaPersistence = !string.IsNullOrEmpty(boundaryTypeName)
                    && sagaBoundarySet.Contains(boundaryTypeName!),
                HasMessagingOutbox = !string.IsNullOrEmpty(boundaryTypeName)
                    && messagingOutboxBoundarySet.Contains(boundaryTypeName!),
                HasBatchProgress = !string.IsNullOrEmpty(boundaryTypeName)
                    && batchBoundarySet.Contains(boundaryTypeName!),
                HasJobPersistence = !string.IsNullOrEmpty(boundaryTypeName)
                    && jobBoundarySet.Contains(boundaryTypeName!),
                HasNotificationStore = !string.IsNullOrEmpty(boundaryTypeName)
                    && notificationBoundarySet.Contains(boundaryTypeName!),
                Entities = entityModels.ToImmutableArray()
            };

            var template = new BoundaryDbContextTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);
        }

        if (entities.Length > 0)
        {
            var allEntities = entities.Where(e => e.IsValid).ToImmutableArray();
            GenerateMigrationDbContexts(context, allEntities, topology, hasI18nEFCore, efCoreProvider,
                assemblyName, traitEntities, hasPrivacyEFCore, hasCryptographyEFCore);
        }
    }

    private static void GenerateMigrationDbContexts(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> allEntities,
        DatabaseTopologyInfo topology,
        bool hasI18nEFCore,
        EfCoreProvider efCoreProvider,
        string? assemblyName = null,
        EquatableArray<Traits.Models.TraitEntityInfo> traitEntities = default,
        bool hasPrivacyEFCore = false,
        bool hasCryptographyEFCore = false)
    {
        if (allEntities.Length == 0)
            return;

        var rootNs = HostArtifactNamespace(assemblyName, allEntities);

        if (topology.HasTopology)
        {
            var databaseGroups = new Dictionary<string, List<EntityMetadataModel>>(StringComparer.Ordinal);
            var unassigned = new List<EntityMetadataModel>();

            var normalizedTopology = new Dictionary<string, DatabaseAssignment>(StringComparer.Ordinal);
            foreach (var kvpT in topology.BoundaryToDatabase)
            {
                var key = kvpT.Key;
                if (key.StartsWith("global::", StringComparison.Ordinal))
                    key = key.Substring(8);
                normalizedTopology[key] = kvpT.Value;
            }

            foreach (var entity in allEntities)
            {
                var boundaryKey = entity.BoundaryTypeFullName ?? "";
                if (boundaryKey.StartsWith("global::", StringComparison.Ordinal))
                    boundaryKey = boundaryKey.Substring(8);

                if (!string.IsNullOrEmpty(boundaryKey) &&
                    normalizedTopology.TryGetValue(boundaryKey, out var assignment))
                {
                    if (!databaseGroups.TryGetValue(assignment.DatabaseTypeName, out var group))
                    {
                        group = new List<EntityMetadataModel>();
                        databaseGroups[assignment.DatabaseTypeName] = group;
                    }

                    group.Add(entity);
                }
                else
                {
                    unassigned.Add(entity);
                }
            }

            // A trait entity belongs to the database of the boundary that owns its parent. Handing
            // every group the whole set put the same tables in every database — two migrations
            // creating GuestComments, BookingTags and the rest side by side, with only one of them
            // ever written to. The BoundaryName was on the model all along and nobody asked it.
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kvp in databaseGroups)
            {
                var assignment = topology.BoundaryToDatabase.Values
                    .First(a => a.DatabaseTypeName == kvp.Key);

                var groupTraits = TraitsOfTheSameBoundaries(kvp.Value, traitEntities);
                foreach (var trait in groupTraits)
                    claimed.Add(trait.FullTypeName);

                EmitMigrationDbContext(context, kvp.Value, rootNs, efCoreProvider,
                    $"{assignment.DatabaseClassName}MigrationDbContext", assignment.DatabaseClassName, hasI18nEFCore,
                    groupTraits, hasPrivacyEFCore, hasCryptographyEFCore);
            }

            // Whatever no database group claimed goes where its parent went: an entity without a
            // boundary is already in `unassigned`, and a table that lands in no context at all is not
            // created by any migration.
            var orphanTraits = traitEntities.IsDefaultOrEmpty
                ? ImmutableArray<Traits.Models.TraitEntityInfo>.Empty
                : traitEntities.Where(t => !claimed.Contains(t.FullTypeName)).ToImmutableArray();

            if (unassigned.Count > 0 || orphanTraits.Length > 0)
                EmitMigrationDbContext(context, unassigned, rootNs, efCoreProvider, "MigrationDbContext", "Migration",
                    hasI18nEFCore, orphanTraits, hasPrivacyEFCore, hasCryptographyEFCore);
        }
        else
        {
            EmitMigrationDbContext(context, allEntities, rootNs, efCoreProvider, "MigrationDbContext", "Migration", hasI18nEFCore,
                traitEntities, hasPrivacyEFCore, hasCryptographyEFCore);
        }
    }

    /// <summary>
    ///     The trait entities whose owning boundary is represented in <paramref name="group" />.
    /// </summary>
    /// <remarks>
    ///     Matched on <c>BoundaryName</c>, the same key the entities in the group carry. A trait whose
    ///     boundary is in no group is not dropped here — the caller gives it to the fallback context,
    ///     because a table nobody's migration creates is worse than one created twice.
    /// </remarks>
    private static ImmutableArray<Traits.Models.TraitEntityInfo> TraitsOfTheSameBoundaries(
        List<EntityMetadataModel> group,
        EquatableArray<Traits.Models.TraitEntityInfo> traitEntities)
    {
        if (traitEntities.IsDefaultOrEmpty)
            return ImmutableArray<Traits.Models.TraitEntityInfo>.Empty;

        var boundaries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in group)
            if (!string.IsNullOrEmpty(entity.BoundaryName))
                boundaries.Add(entity.BoundaryName!);

        return traitEntities
            .Where(t => !string.IsNullOrEmpty(t.BoundaryName) && boundaries.Contains(t.BoundaryName!))
            .ToImmutableArray();
    }

    private static void EmitMigrationDbContext(
        SourceProductionContext context,
        IEnumerable<EntityMetadataModel> entities,
        string ns, EfCoreProvider efCoreProvider, string className, string boundaryName, bool hasI18nEFCore,
        EquatableArray<Traits.Models.TraitEntityInfo> traitEntities = default,
        bool hasPrivacyEFCore = false,
        bool hasCryptographyEFCore = false)
    {
        var entityList = entities.ToList();
        var entityModels = entityList.Select(e => new DbContextEntityModel
        {
            FullTypeName = e.FullTypeName,
            TypeName = e.TypeName,
            DbSetName = StringHelper.Pluralize(e.TypeName),
            ConfigurationTypeName = $"{e.TypeName}EntityConfig",
            IsAbstract = false,
            IsConcurrencyAware = e.IsConcurrencyAware,
            IsTenantEntity = e.IsTenantEntity,
            IsAudited = e.IsAudited,
            IsDataSubject = e.IsDataSubject,
            LogicKeyPropertyName = e.LogicKey,
            LogicKeySelector = e.LogicKeys.Length > 0 ? e.LogicKeySelector : null,
            IsSoftDelete = e.IsSoftDelete,
            TemporalMaxOneParentKey = TemporalMaxOneParentKey(e)
        }).ToList();

        // Add trait entities to migration context
        if (!traitEntities.IsDefaultOrEmpty)
        {
            foreach (var traitEntity in traitEntities)
            {
                entityModels.Add(new DbContextEntityModel
                {
                    FullTypeName = traitEntity.FullTypeName,
                    TypeName = traitEntity.TypeName,
                    DbSetName = StringHelper.Pluralize(traitEntity.TypeName),
                    ConfigurationTypeName = $"{traitEntity.TypeName}EntityConfig",
                    IsAbstract = false,
                    IsConcurrencyAware = false,
                    IsTraitEntity = true,
                });
            }
        }

        var model = new BoundaryDbContextModel
        {
            Namespace = ns,
            ClassName = className,
            BoundaryName = boundaryName,
            IsMigrationContext = true,
            HasI18nEFCore = hasI18nEFCore,
            HasPrivacyEFCore = hasPrivacyEFCore,
                HasCryptographyEFCore = hasCryptographyEFCore,
            EfCoreProvider = efCoreProvider,
            InheritanceConfigurationTypes = BuildInheritanceConfigs(entityList),
            Entities = entityModels.ToImmutableArray()
        };

        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateDbContextRegistration(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities,
        DetectedFeatures detectedFeatures,
        string? assemblyName = null,
        EquatableArray<string> outboxBoundaries = default,
        EquatableArray<string> sagaBoundaries = default,
        EquatableArray<string> messagingOutboxBoundaries = default,
        EquatableArray<string> batchBoundaries = default,
        EquatableArray<string> configurableBoundaries = default,
        EquatableArray<string> jobBoundaries = default,
        EquatableArray<string> notificationBoundaries = default)
    {
        var outboxBoundarySet = outboxBoundaries.AsImmutableArray();
        var sagaBoundarySet = sagaBoundaries.AsImmutableArray();
        var messagingOutboxBoundarySet = messagingOutboxBoundaries.AsImmutableArray();
        var batchBoundarySet = batchBoundaries.AsImmutableArray();
        var configurableBoundarySet = configurableBoundaries.AsImmutableArray();
        var jobBoundarySet = jobBoundaries.AsImmutableArray();
        var notificationBoundarySet = notificationBoundaries.AsImmutableArray();
        if (entities.Length == 0)
            return;
        // Host mode, for the reason spelled out on GenerateBoundaryDbContexts: this registration wires
        // the DbContexts that method emits, so the two must agree on when they exist.
        if (!detectedFeatures.IsHostMode)
            return;

        var entityLookup = entities
            .Where(e => e.IsValid)
            .ToDictionary(e => e.FullTypeName, e => e);

        var boundaryGroups = entities
            .Where(e => e.IsValid && !string.IsNullOrEmpty(e.BoundaryName))
            .GroupBy(e => e.BoundaryName!)
            .ToImmutableArray();

        if (boundaryGroups.Length == 0)
            return;

        var boundaryModels = boundaryGroups
            .Select(group =>
            {
                var boundaryName = group.Key;
                var directEntities = group.ToImmutableArray();
                var ns = BoundaryNamespace(directEntities);

                var boundaryTypeName = directEntities
                    .Select(e => e.BoundaryTypeFullName)
                    .FirstOrDefault(n => !string.IsNullOrEmpty(n));
                if (string.IsNullOrEmpty(boundaryTypeName))
                {
                    boundaryTypeName = string.IsNullOrEmpty(ns)
                        ? $"{boundaryName}Boundary"
                        : $"{ns}.{boundaryName}Boundary";
                }

                return new BoundaryDbContextModel
                {
                    Namespace = ns + ".Entities",
                    ClassName = $"{boundaryName}DbContext",
                    BoundaryName = boundaryName,
                    BoundaryTypeName = boundaryTypeName,
                    IsMigrationContext = false,
                    HasEventOutbox = detectedFeatures.HasEventsEFCore && !string.IsNullOrEmpty(boundaryTypeName)
                        && outboxBoundarySet.Contains(boundaryTypeName!),
                    HasSagaPersistence = !string.IsNullOrEmpty(boundaryTypeName)
                        && sagaBoundarySet.Contains(boundaryTypeName!),
                    HasMessagingOutbox = !string.IsNullOrEmpty(boundaryTypeName)
                        && messagingOutboxBoundarySet.Contains(boundaryTypeName!),
                    HasBatchProgress = !string.IsNullOrEmpty(boundaryTypeName)
                        && batchBoundarySet.Contains(boundaryTypeName!),
                    HasJobPersistence = !string.IsNullOrEmpty(boundaryTypeName)
                        && jobBoundarySet.Contains(boundaryTypeName!),
                    HasNotificationStore = !string.IsNullOrEmpty(boundaryTypeName)
                        && notificationBoundarySet.Contains(boundaryTypeName!),
                    AppliesBoundaryConfiguration = !string.IsNullOrEmpty(boundaryTypeName)
                        && configurableBoundarySet.Contains(boundaryTypeName!),
                    Entities = directEntities.Select(e => new DbContextEntityModel
                    {
                        FullTypeName = e.FullTypeName,
                        TypeName = e.TypeName,
                        DbSetName = StringHelper.Pluralize(e.TypeName),
                        ConfigurationTypeName = $"{e.TypeName}EntityConfig",
                        IsAbstract = false,
                        IsAudited = e.IsAudited
                    }).ToImmutableArray()
                };
            }).ToImmutableArray();

        if (boundaryModels.Length == 0)
            return;

        var prefix = HostArtifactNamespace(assemblyName, entities);

        var template = new DbContextRegistrationTemplate(boundaryModels, prefix, detectedFeatures.HasMultiTenancy,
            detectedFeatures.HasEventsEFCore, detectedFeatures.HasTemporalEfCore);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     Generates EntityConfiguration classes for all valid entities at the host level.
    ///     Only the host knows the database provider, so configs are always generated here
    ///     as internal sealed classes. Entities are deduplicated by FullTypeName.
    /// </summary>
    private static void GenerateEntityConfigurations(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities)
    {
        var generatedConfigs = new HashSet<string>();
        foreach (var entity in entities)
        {
            if (!entity.IsValid)
                continue;
            if (!generatedConfigs.Add(entity.FullTypeName))
                continue;

            var template = new EntityConfigurationTemplate(entity);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ImmutableArray<DbContextEntityModel> BuildReadAccessEntities(
        string? boundaryTypeName,
        EquatableDictionary<string, EquatableArray<string>> readAccessByBoundary,
        Dictionary<string, EntityMetadataModel> entityLookup)
    {
        if (string.IsNullOrEmpty(boundaryTypeName) ||
            !readAccessByBoundary.TryGetValue(boundaryTypeName!, out var readAccessTypeNames))
            return ImmutableArray<DbContextEntityModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<DbContextEntityModel>();
        foreach (var typeName in readAccessTypeNames)
        {
            if (!entityLookup.TryGetValue(typeName, out var entity))
                continue;
            builder.Add(new DbContextEntityModel
            {
                FullTypeName = entity.FullTypeName,
                TypeName = entity.TypeName,
                DbSetName = StringHelper.Pluralize(entity.TypeName),
                ConfigurationTypeName = $"{entity.TypeName}EntityConfig",
                IsAbstract = false,
                // The tenant flag travels, because the "Tenant" named filter is the one this reader's
                // own OnModelCreating has to install: it needs the scoped ITenantContext, which the
                // shared per-entity config cannot reach. Everything else the read-access entity needs
                // does come from that shared config — "SoftDelete", the visibility rules, the indexes —
                // and arrives with ApplyConfiguration. Without this the DbSet [ReadAccess] adds carried
                // !IsDeleted and nothing else, and read every tenant's rows.
                IsTenantEntity = entity.IsTenantEntity,
                // A many-to-many pointing back at the read entity: the target survives — it is the
                // entity being read — while its join entity is ignored, and EF rejects the model at
                // first use. Named here so the reading context can drop the navigation itself.
                SelfReferentialSkipNavigations = entity.Navigations
                    .Where(nav => nav.NavigationType == "ManyToMany"
                                  && nav.TargetFullTypeName == entity.FullTypeName)
                    .Select(nav => nav.Name)
                    .Distinct()
                    .ToImmutableArray()
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> CollectCrossBoundaryTypes(
        ImmutableArray<EntityMetadataModel> directEntities,
        ImmutableArray<DbContextEntityModel> readAccessEntities,
        Dictionary<string, EntityMetadataModel> entityLookup,
        HashSet<string> boundaryEntityFullNames,
        HashSet<string> readAccessEntityNames)
    {
        var directNavTargets = directEntities
            .SelectMany(e => e.Navigations)
            .Where(nav => !nav.IsOwned && // Owned types are part of the parent entity, not cross-boundary
                          !string.IsNullOrEmpty(nav.TargetFullTypeName) &&
                          !boundaryEntityFullNames.Contains(nav.TargetFullTypeName!) &&
                          !readAccessEntityNames.Contains(nav.TargetFullTypeName!))
            .SelectMany(nav =>
            {
                var targets = new List<string> { nav.TargetFullTypeName! };
                if (!string.IsNullOrEmpty(nav.JoinEntityTypeName) &&
                    !boundaryEntityFullNames.Contains(nav.JoinEntityTypeName!) &&
                    !readAccessEntityNames.Contains(nav.JoinEntityTypeName!))
                    targets.Add(nav.JoinEntityTypeName!);
                return targets;
            });

        var readAccessNavTargets = readAccessEntities
            .SelectMany(re =>
            {
                if (!entityLookup.TryGetValue(re.FullTypeName, out var em))
                    return Enumerable.Empty<string>();

                return em.Navigations.SelectMany(nav =>
                {
                    var targets = new List<string>();
                    if (!string.IsNullOrEmpty(nav.TargetFullTypeName) &&
                        !boundaryEntityFullNames.Contains(nav.TargetFullTypeName!) &&
                        !readAccessEntityNames.Contains(nav.TargetFullTypeName!))
                        targets.Add(nav.TargetFullTypeName!);
                    if (!string.IsNullOrEmpty(nav.JoinEntityTypeName) &&
                        !boundaryEntityFullNames.Contains(nav.JoinEntityTypeName!) &&
                        !readAccessEntityNames.Contains(nav.JoinEntityTypeName!))
                        targets.Add(nav.JoinEntityTypeName!);
                    return targets;
                });
            });

        return directNavTargets.Concat(readAccessNavTargets).Distinct().ToImmutableArray();
    }

    /// <summary>
    ///     The trait entities of <b>another</b> boundary that this context would discover through an
    ///     entity it only borrows, and must therefore ignore.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ignored rather than configured, because the rows belong to the owning boundary: a
    ///         context that borrows an entity in order to read it has no business writing that
    ///         entity's tags or comments.
    ///     </para>
    /// </remarks>
    private static IEnumerable<string> BorrowedTraitEntities(
        EquatableArray<Traits.Models.TraitEntityInfo> traitEntities,
        string boundaryName,
        ImmutableArray<DbContextEntityModel> readAccessEntities)
    {
        if (traitEntities.AsImmutableArray().IsDefaultOrEmpty || readAccessEntities.IsDefaultOrEmpty)
            return Enumerable.Empty<string>();

        var borrowed = new HashSet<string>(
            readAccessEntities.Select(e => e.TypeName), StringComparer.Ordinal);

        return traitEntities
            .Where(t => !string.Equals(t.BoundaryName, boundaryName, StringComparison.Ordinal))
            .Where(t => t.Relations.AsImmutableArray()
                .Any(r => borrowed.Contains(r.ReferencedTypeName)))
            .Select(t => t.FullTypeName);
    }

    private static ImmutableArray<EntityMetadataModel> CollectRequiredEntities(
        ImmutableArray<EntityMetadataModel> directEntities,
        Dictionary<string, EntityMetadataModel> entityLookup)
    {
        var entityByTypeName = entityLookup.Values
            .GroupBy(e => e.TypeName)
            .ToDictionary(g => g.Key, g => g.First());

        var visited = new HashSet<string>();
        var result = new List<EntityMetadataModel>();
        var toVisit = new Queue<EntityMetadataModel>(directEntities);

        while (toVisit.Count > 0)
        {
            var entity = toVisit.Dequeue();
            if (visited.Contains(entity.FullTypeName))
                continue;
            visited.Add(entity.FullTypeName);
            result.Add(entity);

            foreach (var nav in entity.Navigations)
                if (entityLookup.TryGetValue(nav.TargetTypeName, out var relatedEntity))
                    if (!visited.Contains(relatedEntity.FullTypeName))
                        toVisit.Enqueue(relatedEntity);

            foreach (var prop in entity.Properties)
            {
                if (prop.Name is "PersistenceId" or "Id")
                    continue;
                if (prop.Name.EndsWith("Id", StringComparison.Ordinal) && prop.Name.Length > 2)
                {
                    var potentialEntityName = prop.Name.Substring(0, prop.Name.Length - 2);
                    if (entityByTypeName.TryGetValue(potentialEntityName, out var relatedEntity))
                        if (!visited.Contains(relatedEntity.FullTypeName))
                            toVisit.Enqueue(relatedEntity);
                }
            }
        }

        return result.ToImmutableArray();
    }

    internal static ImmutableArray<string> BuildInheritanceConfigs(IEnumerable<EntityMetadataModel> entities)
    {
        // Only reference a {Type}InheritanceConfiguration for the ROOT of a hierarchy, which is what
        // InheritanceMappingTemplate actually generates. A derived leaf carrying [Inheritance(
        // DiscriminatorValue=...)] also has a non-empty InheritanceStrategy but generates no config of
        // its own (it has an entity base type), so referencing it would produce a CS0103 in the
        // BoundaryDbContext. Roots have no entity base type.
        return entities
            .Where(e => !string.IsNullOrEmpty(e.InheritanceStrategy) && string.IsNullOrEmpty(e.BaseEntityFullTypeName))
            .Select(e =>
            {
                var configName = NamingHelper.AppendSuffix(e.TypeName, "InheritanceConfiguration");
                return $"{e.Namespace}.{configName}";
            })
            .ToImmutableArray();
    }

    /// <summary>
    /// Resolves properties of a trait entity from Compilation for schema metadata.
    /// </summary>
    private static ImmutableArray<PropertyMetadataModel> ResolveTraitEntityProperties(
        Compilation compilation, Traits.Models.TraitEntityInfo traitEntity)
    {
        var symbol = compilation.GetTypeByMetadataName(traitEntity.FullTypeName);
        if (symbol is null) return ImmutableArray<PropertyMetadataModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<PropertyMetadataModel>();
        var visited = new HashSet<string>();

        // Walk the type hierarchy (entity + base classes)
        var current = symbol;
        while (current is not null && current.Name != "Object")
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.IsStatic || prop.IsIndexer) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (!visited.Add(prop.Name)) continue;
                // Skip PersistenceId (alias), DomainEvents, ModifiedProperties
                // Also skip Id — it's the PK, handled by SchemaMetadataTransform as "PersistenceId" column
                if (prop.Name is "PersistenceId" or "Id" or "DomainEvents" or "ModifiedProperties") continue;

                // Skip collections (navigation properties)
                if (prop.Type is INamedTypeSymbol named &&
                    named.Name is "ICollection" or "IList" or "List" or "IEnumerable") continue;

                // Skip navigation properties (class types with Id that are not string/value types).
                // The key member has to be looked for up the whole hierarchy: a trait-generated
                // target such as {Boundary}Tag declares nothing of its own and inherits Id from
                // TagBase, so a declared-members-only probe mistakes it for a scalar and emits a
                // bogus column named after the navigation.
                if (prop.Type is INamedTypeSymbol navType &&
                    navType.TypeKind == TypeKind.Class &&
                    navType.SpecialType != SpecialType.System_String &&
                    HasKeyMember(navType))
                    continue;

                // Skip abstract base ParentEntityId (the concrete FK is generated per-entity)
                if (prop.Name == "ParentEntityId") continue;

                var typeName = prop.Type.ToDisplayString();
                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                                 prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                var isActualEnum = prop.Type.TypeKind == TypeKind.Enum ||
                                   (prop.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable &&
                                    nullable.TypeArguments[0].TypeKind == TypeKind.Enum);

                // Trait entities store enums as string (HasConversion<string>) — report as string to schema
                var effectiveType = isActualEnum ? "string" : typeName;
                var effectiveMaxLength = isActualEnum ? (int?)32 : null;

                builder.Add(new PropertyMetadataModel
                {
                    Name = prop.Name,
                    TypeName = effectiveType,
                    IsNullable = isNullable,
                    IsEnum = false, // Stored as string, not enum
                    HasPrivateSetter = prop.SetMethod?.DeclaredAccessibility != Accessibility.Public,
                    MaxLength = effectiveMaxLength,
                });
            }

            current = current.BaseType;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Splits the comma-separated key column list carried by the trait metadata.
    ///     Empty when the entity uses the surrogate <c>PersistenceId</c> key.
    /// </summary>
    private static EquatableArray<string> ParseKeyColumns(string? keyColumns)
    {
        if (string.IsNullOrWhiteSpace(keyColumns))
            return EquatableArray<string>.Empty;

        return keyColumns!
            .Split(',')
            .Select(static c => c.Trim())
            .Where(static c => c.Length > 0)
            .ToImmutableArray();
    }

    /// <summary>
    ///     Turns the relationships a trait entity declares into the navigation shape the schema
    ///     transform understands, so a trait table gets the same foreign keys and FK indexes as any
    ///     hand-written entity. The referenced type name stays singular: <c>BuildForeignKeys</c>
    ///     pluralizes it the same way it does for every other entity, so the two stay in step.
    /// </summary>
    private static EquatableArray<NavigationMetadataModel> BuildTraitNavigations(
        Features.Traits.Models.TraitEntityInfo traitEntity, string? boundaryFqn)
    {
        if (traitEntity.Relations.Length == 0)
            return EquatableArray<NavigationMetadataModel>.Empty;

        var navigations = ImmutableArray.CreateBuilder<NavigationMetadataModel>();
        foreach (var relation in traitEntity.Relations)
        {
            navigations.Add(new NavigationMetadataModel
            {
                Name = relation.ReferencedTypeName,
                TargetTypeName = relation.ReferencedTypeName,
                NavigationType = "ManyToOne",
                ForeignKeyProperty = relation.ForeignKeyColumn,
                OnDelete = relation.OnDelete,
                IsRequired = true,
                // Same boundary as the trait entity: a trait table always lives with its parent, so
                // the cross-boundary guard in BuildForeignKeys must not skip it.
                TargetBoundaryTypeFullName = boundaryFqn,
            });
        }

        return navigations.ToImmutable();
    }

    /// <summary>
    ///     Whether the type — or any of its base types — exposes an <c>Id</c>/<c>PersistenceId</c>
    ///     property, i.e. whether it looks like an entity rather than a scalar.
    /// </summary>
    private static bool HasKeyMember(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetMembers().Any(m => m is IPropertySymbol { Name: "Id" or "PersistenceId" }))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The namespace the host's schema and migration artifacts are generated into.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The <b>assembly</b>, not an entity. The namespace of whichever entity happens to be
    ///         first in the list agrees with what the host entry point names only while every entity
    ///         lives under the application's root. An imported package owns entities too, and
    ///         <c>Pragmatic.Authorization.Management.Entities</c> sorts before <c>Showcase.*</c>: the
    ///         schema class would move to <c>Pragmatic.Authorization</c> and the host would stop
    ///         compiling, naming a type in a namespace nobody wrote.
    ///     </para>
    ///     <para>
    ///         The consumer side derives the same name from the module type — <c>Showcase.Booking.BookingModule</c>
    ///         → <c>Showcase</c> — and this is the producer's copy of that rule. Two derivations of one
    ///         name, and they agree for <c>{Root}.Host</c> and for a host that is the root itself.
    ///     </para>
    /// </remarks>
    private static string HostArtifactNamespace(string? assemblyName, ImmutableArray<EntityMetadataModel> entities)
    {
        // An entity of the application, never one adopted from an imported package: the consumer of
        // these names — the generated host entry — derives them from a module type, and a package is
        // not a module of this application.
        var representative = entities.FirstOrDefault(e => !e.IsFromImportedPackage)
                             ?? entities.FirstOrDefault();

        if (representative is not null)
            return DeriveRootNamespace(representative.Namespace);

        return string.IsNullOrEmpty(assemblyName) ? "" : DeriveRootNamespace(assemblyName!);
    }

    /// <summary>
    ///     The namespace a boundary's generated <c>DbContext</c> is declared in.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The <b>boundary's</b> namespace, not the first entity's. They are the same for an
    ///     ordinary module, and they part the moment a boundary adopts the entities of an imported
    ///     package: <c>AccountsDbContext</c> moved to <c>Pragmatic.Authorization.Management.Entities</c>
    ///     because <c>DynamicPermission</c> sorted first, and every hand-written reference to it stopped
    ///     compiling. The boundary is what the context is named after, so it is what the namespace
    ///     follows.
    /// </remarks>
    private static string BoundaryNamespace(ImmutableArray<EntityMetadataModel> directEntities)
    {
        var boundaryType = directEntities
            .Select(e => e.BoundaryTypeFullName)
            .FirstOrDefault(n => !string.IsNullOrEmpty(n));

        if (!string.IsNullOrEmpty(boundaryType))
        {
            var plain = boundaryType!.StartsWith("global::", StringComparison.Ordinal)
                ? boundaryType.Substring(8)
                : boundaryType;
            var lastDot = plain.LastIndexOf('.');
            if (lastDot > 0)
                return plain.Substring(0, lastDot);
        }

        var ns = directEntities.FirstOrDefault()?.Namespace ?? "";
        return ns.EndsWith(".Entities", StringComparison.Ordinal)
            ? ns.Substring(0, ns.Length - ".Entities".Length)
            : ns;
    }

    private static string DeriveRootNamespace(string ns)
    {
        if (ns.EndsWith(".Entities", StringComparison.Ordinal))
            ns = ns.Substring(0, ns.Length - ".Entities".Length);

        var parts = ns.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts.Take(parts.Length - 1)) : ns;
    }

    // =========================================================================
    // Schema metadata generation (Pragmatic.Migrations integration)
    // =========================================================================

    private static void GenerateSchemaMetadata(
        SourceProductionContext context,
        (((ImmutableArray<EntityMetadataModel> Entities,
            EquatableDictionary<string, EquatableArray<string>> ReadAccessByBoundary) Input,
            bool HasI18nEFCore),
            DatabaseTopologyInfo Topology) input,
        EfCoreProvider efCoreProvider,
        SchemaMetadataTransform.SchemaResolutionContext schemaContext,
        bool isHostMode,
        string? assemblyName = null,
        EquatableArray<string> outboxBoundaries = default,
        EquatableArray<string> sagaBoundaries = default,
        EquatableArray<string> messagingOutboxBoundaries = default,
        EquatableArray<string> batchBoundaries = default,
        bool hasPrivacyEFCore = false,
        bool hasCryptographyEFCore = false,
        EquatableArray<string> jobBoundaries = default,
        EquatableArray<string> notificationBoundaries = default)
    {
        var entities = input.Item1.Input.Entities;
        var topology = input.Topology;

        if (entities.Length == 0)
            return;

        // Host mode, for the reason spelled out on GenerateBoundaryDbContexts. The per-entity filter goes
        // with it: the schema of a host's database covers every entity that lands in its DbContexts, and
        // where an entity was declared is not what decides that.
        if (!isHostMode)
            return;

        var validEntities = entities.Where(e => e.IsValid).ToImmutableArray();
        if (validEntities.Length == 0)
            return;

        // A database needs the __EventOutbox table when any of its boundaries is [EnableEventOutbox].
        static string StripGlobalPrefix(string s) =>
            s.StartsWith("global::", StringComparison.Ordinal) ? s.Substring(8) : s;
        var outboxBoundarySet = new HashSet<string>(
            outboxBoundaries.AsImmutableArray().Select(StripGlobalPrefix), StringComparer.Ordinal);
        bool HasOutbox(IEnumerable<EntityMetadataModel> group) => outboxBoundarySet.Count > 0
            && group.Any(e => outboxBoundarySet.Contains(StripGlobalPrefix(e.BoundaryTypeFullName ?? "")));

        // A database needs the saga tables when any of its boundaries is [EnableSagaPersistence].
        var sagaBoundarySet = new HashSet<string>(
            sagaBoundaries.AsImmutableArray().Select(StripGlobalPrefix), StringComparer.Ordinal);
        bool HasSaga(IEnumerable<EntityMetadataModel> group) => sagaBoundarySet.Count > 0
            && group.Any(e => sagaBoundarySet.Contains(StripGlobalPrefix(e.BoundaryTypeFullName ?? "")));

        // A database needs the __OutboxMessages table when any of its boundaries is [EnableOutbox].
        var messagingOutboxBoundarySet = new HashSet<string>(
            messagingOutboxBoundaries.AsImmutableArray().Select(StripGlobalPrefix), StringComparer.Ordinal);
        bool HasMessagingOutbox(IEnumerable<EntityMetadataModel> group) => messagingOutboxBoundarySet.Count > 0
            && group.Any(e => messagingOutboxBoundarySet.Contains(StripGlobalPrefix(e.BoundaryTypeFullName ?? "")));

        // A database needs the __BatchProgress table when its (single-owner) boundary is [EnableBatchProgress].
        var batchBoundarySet = new HashSet<string>(
            batchBoundaries.AsImmutableArray().Select(StripGlobalPrefix), StringComparer.Ordinal);
        bool HasBatch(IEnumerable<EntityMetadataModel> group) => batchBoundarySet.Count > 0
            && group.Any(e => batchBoundarySet.Contains(StripGlobalPrefix(e.BoundaryTypeFullName ?? "")));

        // A database needs __Jobs/__RecurringJobs when its (single-owner) boundary is [EnableJobPersistence].
        var jobBoundarySet = new HashSet<string>(
            jobBoundaries.AsImmutableArray().Select(StripGlobalPrefix), StringComparer.Ordinal);
        bool HasJobs(IEnumerable<EntityMetadataModel> group) => jobBoundarySet.Count > 0
            && group.Any(e => jobBoundarySet.Contains(StripGlobalPrefix(e.BoundaryTypeFullName ?? "")));

        // A database needs __Notifications when one of its boundaries is [StoresNotifications]. ⚠️ The
        // boundary's own DbContext maps the table and that is not enough: the schema an application
        // creates comes from the MIGRATION context, and a table with no mirror here does not exist —
        // `relation "__Notifications" does not exist` on the first send.
        var notificationBoundarySet = new HashSet<string>(
            notificationBoundaries.AsImmutableArray().Select(StripGlobalPrefix), StringComparer.Ordinal);
        bool HasNotifications(IEnumerable<EntityMetadataModel> group) => notificationBoundarySet.Count > 0
            && group.Any(e => notificationBoundarySet.Contains(StripGlobalPrefix(e.BoundaryTypeFullName ?? "")));

        var rootNs = HostArtifactNamespace(assemblyName, validEntities);

        if (topology.HasTopology)
        {
            // Multi-database: group entities by database via topology
            var normalizedTopology = new Dictionary<string, DatabaseAssignment>(StringComparer.Ordinal);
            foreach (var kvpT in topology.BoundaryToDatabase)
            {
                var key = kvpT.Key;
                if (key.StartsWith("global::", StringComparison.Ordinal))
                    key = key.Substring(8);
                normalizedTopology[key] = kvpT.Value;
            }

            var databaseGroups = new Dictionary<string, List<EntityMetadataModel>>(StringComparer.Ordinal);
            foreach (var entity in validEntities)
            {
                var boundaryKey = entity.BoundaryTypeFullName ?? "";
                if (boundaryKey.StartsWith("global::", StringComparison.Ordinal))
                    boundaryKey = boundaryKey.Substring(8);

                if (!string.IsNullOrEmpty(boundaryKey) &&
                    normalizedTopology.TryGetValue(boundaryKey, out var assignment))
                {
                    if (!databaseGroups.TryGetValue(assignment.DatabaseClassName, out var group))
                    {
                        group = new List<EntityMetadataModel>();
                        databaseGroups[assignment.DatabaseClassName] = group;
                    }
                    group.Add(entity);
                }
            }

            foreach (var kvp in databaseGroups)
            {
                // Resolve ConfigKey from topology assignment
                var configKey = topology.BoundaryToDatabase.Values
                    .FirstOrDefault(a => a.DatabaseClassName == kvp.Key)?.ConfigKey;
                EmitSchemaMetadata(context, kvp.Value.ToImmutableArray(), efCoreProvider, kvp.Key, rootNs, schemaContext,
                    configKey, HasOutbox(kvp.Value), HasSaga(kvp.Value), HasMessagingOutbox(kvp.Value), HasBatch(kvp.Value),
                    hasPrivacyEFCore, hasCryptographyEFCore, HasJobs(kvp.Value), HasNotifications(kvp.Value));
            }
        }
        else
        {
            // Single database: all entities → one SchemaMetadata
            EmitSchemaMetadata(context, validEntities, efCoreProvider, "Default", rootNs, schemaContext,
                hasEventOutbox: HasOutbox(validEntities), hasSagaPersistence: HasSaga(validEntities),
                hasMessagingOutbox: HasMessagingOutbox(validEntities), hasBatchProgress: HasBatch(validEntities),
                hasPrivacyEFCore: hasPrivacyEFCore, hasCryptographyEFCore: hasCryptographyEFCore,
                hasJobPersistence: HasJobs(validEntities), hasNotificationStore: HasNotifications(validEntities));
        }
    }

    /// <summary>
    ///     Builds a resolution context with owned type properties and TPH derived types
    ///     from the Compilation. Passed as parameter to SchemaMetadataTransform (no static state).
    /// </summary>
    private static SchemaMetadataTransform.SchemaResolutionContext BuildSchemaResolutionContext(
        Compilation compilation,
        (((ImmutableArray<EntityMetadataModel> Entities,
            EquatableDictionary<string, EquatableArray<string>> ReadAccessByBoundary) Input,
            bool HasI18nEFCore),
            DatabaseTopologyInfo Topology) input)
    {
        var ownedTypes = new Dictionary<string, List<(string Name, string TypeName, bool IsNullable, bool IsEnum)>>();

        foreach (var entity in input.Item1.Input.Entities)
        {
            foreach (var nav in entity.Navigations)
            {
                if (!nav.IsOwned) continue;

                var targetSimple = nav.TargetTypeName.Contains('.')
                    ? nav.TargetTypeName.Substring(nav.TargetTypeName.LastIndexOf('.') + 1)
                    : nav.TargetTypeName;

                if (ownedTypes.ContainsKey(targetSimple)) continue;

                // Resolve the owned type from compilation
                var targetFqn = nav.TargetFullTypeName?.Replace("global::", "") ?? nav.TargetTypeName;
                var typeSymbol = compilation.GetTypeByMetadataName(targetFqn);
                if (typeSymbol is null) continue;

                var props = new List<(string, string, bool, bool)>();
                var currentType = typeSymbol;

                // Walk inheritance chain to collect all properties
                while (currentType is not null && currentType.SpecialType != SpecialType.System_Object)
                {
                    foreach (var member in currentType.GetMembers())
                    {
                        if (member is not IPropertySymbol prop) continue;
                        if (prop.IsStatic || prop.IsIndexer) continue;
                        if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                        if (prop.GetMethod is null) continue;

                        // Skip navigation-like properties and known non-mapped
                        var propTypeName = prop.Type.ToDisplayString();
                        if (propTypeName.Contains("IReadOnlyList") || propTypeName.Contains("ICollection"))
                            continue;

                        var isNullable = prop.NullableAnnotation == NullableAnnotation.Annotated
                                         || prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                        var isEnum = prop.Type.TypeKind == TypeKind.Enum;

                        props.Add((prop.Name, propTypeName, isNullable, isEnum));
                    }

                    currentType = currentType.BaseType;
                }

                ownedTypes[targetSimple] = props;
            }
        }

        // Resolve TPH derived types
        var tphDerived = ResolveTphDerivedTypes(compilation, input.Item1.Input.Entities);

        return new SchemaMetadataTransform.SchemaResolutionContext
        {
            OwnedTypeProperties = ownedTypes,
            TphDerivedTypes = tphDerived
        };
    }

    /// <summary>
    ///     For TPH base entities, discovers derived types from the Compilation and collects their
    ///     declared-only properties. These columns will be merged into the base table as nullable.
    /// </summary>
    private static Dictionary<string, List<SchemaMetadataTransform.TphDerivedInfo>> ResolveTphDerivedTypes(
        Compilation compilation, ImmutableArray<EntityMetadataModel> entities)
    {
        var tphDerived = new Dictionary<string, List<SchemaMetadataTransform.TphDerivedInfo>>();

        foreach (var entity in entities)
        {
            if (entity.InheritanceStrategy != "TPH") continue;

            var baseSymbol = compilation.GetTypeByMetadataName(entity.FullTypeName);
            if (baseSymbol is null) continue;

            var derivedInfos = new List<SchemaMetadataTransform.TphDerivedInfo>();
            var basePropertyNames = new HashSet<string>(
                baseSymbol.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name));

            // Scan all types in compilation to find derived types
            CollectDerivedTypeProperties(compilation.GlobalNamespace, baseSymbol, basePropertyNames, derivedInfos);

            if (derivedInfos.Count > 0)
                tphDerived[entity.FullTypeName] = derivedInfos;
        }

        return tphDerived;
    }

    private static void CollectDerivedTypeProperties(
        INamespaceSymbol ns,
        INamedTypeSymbol baseSymbol,
        HashSet<string> basePropertyNames,
        List<SchemaMetadataTransform.TphDerivedInfo> results,
        HashSet<INamespaceSymbol>? visitedNamespaces = null)
    {
        visitedNamespaces ??= new HashSet<INamespaceSymbol>(SymbolEqualityComparer.Default);
        if (!visitedNamespaces.Add(ns)) return; // Cycle detection

        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                CollectDerivedTypeProperties(childNs, baseSymbol, basePropertyNames, results, visitedNamespaces);
            }
            else if (member is INamedTypeSymbol typeSymbol)
            {
                if (typeSymbol.IsAbstract) continue;
                if (!InheritsFrom(typeSymbol, baseSymbol)) continue;

                // Collect declared-only properties (not inherited from base)
                var props = new List<(string Name, string TypeName, bool IsNullable, bool IsEnum)>();
                foreach (var prop in typeSymbol.GetMembers().OfType<IPropertySymbol>())
                {
                    if (prop.IsStatic || prop.IsIndexer) continue;
                    if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                    if (prop.GetMethod is null) continue;
                    if (basePropertyNames.Contains(prop.Name)) continue;

                    var propTypeName = prop.Type.ToDisplayString();
                    if (propTypeName.Contains("IReadOnlyList") || propTypeName.Contains("ICollection"))
                        continue;

                    var isNullable = prop.NullableAnnotation == NullableAnnotation.Annotated
                                     || prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                    var isEnum = prop.Type.TypeKind == TypeKind.Enum;

                    props.Add((prop.Name, propTypeName, isNullable, isEnum));
                }

                if (props.Count > 0)
                    results.Add(new SchemaMetadataTransform.TphDerivedInfo { Properties = props });
            }
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
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

    private static void EmitSchemaMetadata(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities,
        EfCoreProvider provider,
        string databaseName,
        string rootNamespace,
        SchemaMetadataTransform.SchemaResolutionContext schemaContext,
        string? configKey = null,
        bool hasEventOutbox = false,
        bool hasSagaPersistence = false,
        bool hasMessagingOutbox = false,
        bool hasBatchProgress = false,
        bool hasPrivacyEFCore = false,
        bool hasCryptographyEFCore = false,
        bool hasJobPersistence = false,
        bool hasNotificationStore = false)
    {
        var model = SchemaMetadataTransform.Transform(entities, provider, databaseName, rootNamespace, schemaContext, configKey, hasEventOutbox, hasSagaPersistence, hasMessagingOutbox, hasBatchProgress, hasPrivacyEFCore, hasCryptographyEFCore, hasJobPersistence, hasNotificationStore);
        var template = new SchemaMetadataTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     The parent key whose open stretch must be unique, or null when no index can say it.
    /// </summary>
    /// <remarks>
    ///     Only <c>MaxActive = 1</c> scoped to a parent: that is exactly "one row per parent with
    ///     ValidTo null", which a partial unique index states. A global MaxActive, or one above 1, has
    ///     no index that expresses it and stays with the pipeline check.
    /// </remarks>
    private static string? TemporalMaxOneParentKey(Models.EntityMetadataModel entity)
        => entity is { IsTemporalRelation: true, TemporalMaxActive: 1, TemporalParentFkProperty: not null }
            ? entity.TemporalParentFkProperty
            : null;

}
