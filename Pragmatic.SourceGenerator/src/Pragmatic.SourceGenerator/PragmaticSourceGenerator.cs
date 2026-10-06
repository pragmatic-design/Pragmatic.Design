using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions;
using Pragmatic.SourceGenerator.Features.Caching;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Configuration;
using Pragmatic.SourceGenerator.Features.Endpoints;
using Pragmatic.SourceGenerator.Features.I18n;
using Pragmatic.SourceGenerator.Features.Mapping;
using Pragmatic.SourceGenerator.Features.Migrations;
using Pragmatic.SourceGenerator.Features.Patch;
using Pragmatic.SourceGenerator.Features.Persistence;
using Pragmatic.SourceGenerator.Features.Result;
using Pragmatic.SourceGenerator.Features.Identity;
using Pragmatic.SourceGenerator.Features.FastEnum;
using Pragmatic.SourceGenerator.Features.Jobs;
using Pragmatic.SourceGenerator.Features.Lifecycle;
using Pragmatic.SourceGenerator.Features.Manifest;
using Pragmatic.SourceGenerator.Features.Messaging;
using Pragmatic.SourceGenerator.Features.Resource;
using Pragmatic.SourceGenerator.Features.Specification;
using Pragmatic.SourceGenerator.Features.Temporal;
using Pragmatic.SourceGenerator.Features.Traits;
using Pragmatic.SourceGenerator.Features.Validation;
using Pragmatic.SourceGenerator.Features.ValueObject;

namespace Pragmatic.SourceGenerator;

/// <summary>
/// Unified Pragmatic source generator.
/// Detects which runtime packages are referenced and activates the corresponding feature pipelines.
///
/// Virtual folder convention for generated files:
///   {Feature}/{NamespaceSegment}/{Type}.{Artifact}.g.cs   (per-type)
///   {Feature}/{Prefix}.{Extension}.g.cs                   (per-assembly)
///   Metadata/{Prefix}.PragmaticManifest.g.cs              (manifest)
/// </summary>
[Generator]
public sealed class PragmaticSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Detect which features are available — ONCE per compilation.
        var features = context.CompilationProvider
            .Select(static (compilation, _) => FeatureDetector.Detect(compilation));

        // Feature modules — registered as migrations complete.
        // Each feature guards itself via the DetectedFeatures snapshot.
        var (resourceModels, resourceQueries, resourceEndpoints, resourceMutations, resourceMappings, resourceManifestTypes) = ResourceFeature.Register(context, features);
        var traitOutput = TraitFeature.Register(context, features);
        MigrationsFeature.Register(context, features);
        // Caching contributes the [InvalidatesCache] domain-event handlers it generates, so Composition
        // can register them: a generator cannot resolve a symbol it creates in the same compilation, so
        // those handlers have to be described by whoever renders them. Registration order is irrelevant
        // to execution; it only makes the provider available below.
        var (cacheInvalidationHandlers, cachingRegistrations) = CachingFeature.Register(context, features);

        // Persistence registers BEFORE ActionsFeature so its generated entity-permission catalog
        // can resolve [RequirePermission(BookingPermissions.Entity.Op)] references in the Actions
        // pipeline — a source generator cannot resolve constants it generates itself in the same
        // compilation, so those references would otherwise fail-open (permission silently not enforced).
        // Registration order does not affect execution; it only makes the catalog provider available.
        var allProgrammaticQueries = traitOutput.Queries is not null
            ? resourceQueries.Combine(traitOutput.Queries.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right))
            : resourceQueries;
        var (entityModels, persistenceRegistrations, derivedQueryEndpoints, queryModels, persistedStores) = PersistenceFeature.Register(context, features,
            resourceQueries: allProgrammaticQueries,
            localTraitEntities: traitOutput.TraitEntities);

        // The custom permissions the assembly declares — [assembly: Permission], [RequirePermission(Description)].
        // Their constants and the entities' CRUD ones are generated in this run, so nothing binds them here:
        // the catalogue of const path → value is built once, from the same input the permissions class is
        // rendered from, and handed to every feature that resolves a constant — roles included.
        var customPermissions = IdentityFeature.DeclaredPermissions(context);
        var customValues = customPermissions.Select(static (declared, _) =>
            declared.Select(static d => d.Value).ToImmutableArray());

        var permissionCatalog = entityModels.Combine(customValues)
            .Select(static (pair, _) => new EquatableArray<PermissionConstEntry>(
                PermissionCatalogBuilder.Build(pair.Left, pair.Right)));

        var (identityRegistrations, currentUsers) =
            IdentityFeature.Register(context, features, permissionCatalog, customPermissions);

        // The one {Boundary}Permissions class per boundary: the CRUD constants and the declared ones.
        Features.Persistence.PermissionsClassFeature.Register(context, features, entityModels, customPermissions);

        // Only traits contribute actions now: [Resource] scaffolds mutations for the writes and
        // Single queries for the reads, so its operations reach the pipelines a hand-written one does.
        var (actionsRegistrations, derivedPermissions, localDomainModules, permissionDerivation) = ActionsFeature.Register(
            context, features, traitActions: traitOutput.Actions, permissionCatalog: permissionCatalog,
            programmaticMutations: resourceMutations, queries: queryModels, currentUsers: currentUsers);
        var patchManifestTypes = PatchFeature.Register(context, features);


        // Declared resilience. Registered unconditionally and driven by the attributes, for the
        // same reason as redaction above and one more: what it publishes is the answer to «did
        // anybody ask for this», which a DetectedFeatures flag built from TypeExists cannot give.
        var resilienceRegistrations = Features.Resilience.ResilienceFeature.Register(context);
        var featureFlagRegistrations = Features.FeatureFlags.FeatureFlagsFeature.Register(context);
        var pdxTemplateRegistrations = Features.Documents.DocumentsFeature.Register(context);
        var validationRegistrations = ValidationFeature.Register(context, features);
        MappingFeature.Register(context, features, programmaticMappings: resourceMappings);
        // Combine resource + trait endpoints for EndpointsFeature injection
        var allProgrammaticEndpoints = traitOutput.Endpoints is not null
            ? resourceEndpoints.Combine(traitOutput.Endpoints.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right))
            : resourceEndpoints;
        // …and the routes derived from specifications, which are in the same position: a model, because
        // the type they execute is written by this generator and is not a symbol anything can resolve.
        allProgrammaticEndpoints = allProgrammaticEndpoints.Combine(derivedQueryEndpoints)
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        // Every type this generator creates has to describe itself to the manifest: nothing can resolve a
        // symbol that does not exist yet in the compilation being analysed.
        var allProgrammaticTypes = traitOutput.ManifestTypes is not null
            ? resourceManifestTypes.Combine(traitOutput.ManifestTypes.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right))
            : resourceManifestTypes;
        // Patch DTOs are declared by the user as an empty partial record and filled in entirely by the
        // generator, so they are in the same position as the Resource and Trait DTOs above.
        allProgrammaticTypes = allProgrammaticTypes.Combine(patchManifestTypes)
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        var (endpointModels, endpointRegistrations, allDerivedPermissions) = EndpointsFeature.Register(context, features,
            programmaticEndpoints: allProgrammaticEndpoints,
            programmaticTypes: allProgrammaticTypes,
            permissionCatalog: permissionCatalog,
            derivedPermissions: derivedPermissions,
            derivation: permissionDerivation);
        // The queries' invokers, emitted here rather than inside Persistence, and after Actions: a
        // [RequirePermission] on a query often names a constant this generator writes, and the catalog
        // that binds it is built from the entity models Persistence has just produced — while the
        // auto-derivation posture's inputs come from Actions. The invoker needs both: it is the door
        // every caller uses, and a permission written only on the route leaves the in-process one open.
        // The user entities too: [FromCurrentUser] binds a member of one, and the invoker fills it.
        Features.Persistence.QueryFeature.RegisterInvokers(
            context, queryModels, permissionCatalog, features, permissionDerivation, currentUsers);

        // The catalog of every name the posture produced — actions, mutations and queries — so a role
        // can grant them. Here and not inside Actions, because the queries are derived by Endpoints,
        // which runs after Actions and needs what Actions returns.
        var derivedPermissionRegistrations = ActionsFeature.RegisterDerivedPermissionCatalog(context, allDerivedPermissions);
        ResultFeature.Register(context, features);
        I18nFeature.Register(context, features);
        var readContractRegistrations = ReadContractFeature.Register(context);
        var rollUpRegistrations = RollUpFeature.Register(context);
        var configurationRegistrations = ConfigurationFeature.Register(context, features);
        var messagingRegistrations = MessagingFeature.Register(context, features);
        // Endpoints are passed in for the request bodies this generator emits: they exist only as models,
        // never as symbols, so the JSON context cannot find them on its own. The redacted types too: the
        // redactor serializes them inside the logger, where a reflection failure under Native AOT
        // loses the entry.
        var (serializationRegistrations, jsonContextEmitted) =
            Features.Serialization.SerializationFeature.Register(context, features, endpoints: endpointModels,
                redactedTypes: Features.Redaction.RedactionFeature.JsonRoots(context));

        // Declared redaction. Registered unconditionally and driven by the attributes rather than by
        // a DetectedFeatures flag: [NotLogged] lives in Abstractions, which every module references,
        // and a map gated on a feature being "on" would reintroduce the gap it exists to close. After
        // the JSON context, because the map hands that context to the redactor when it exists.
        var redactionRegistrations = Features.Redaction.RedactionFeature.Register(context, jsonContextEmitted);
        // Endpoints are passed in because the conversion is keyed on the type that is
        // deserialized, and for an operation with body properties that is the generated {Trigger}Body
        // record rather than the operation the attribute sits on.
        var temporalRegistrations = TemporalFeature.Register(context, features, endpoints: endpointModels);
        // Endpoints are passed in for PRAG2904: whether special-category data is over-exposed cannot be
        // decided from the entity alone, only from what reaches it.
        var privacyRegistrations =
            Features.Privacy.PrivacyFeature.Register(context, features, endpoints: endpointModels
                // The signed-in user's entity, for the operations that load it: Identity's to say which.
                .Combine(currentUsers)
                .Select(static (pair, _) => Features.Privacy.Transforms.SignedInUserReach.Resolve(pair.Left, pair.Right)));

        // Manifest generation is handled inline in EndpointsFeature (pipeline compatibility)

        // Standalone features — no runtime package dependency needed
        var fastEnumRegistrations = FastEnumFeature.Register(context);
        var (jobsRegistrations, jobServices) = JobsFeature.Register(context, programmaticJobs: traitOutput.Jobs);

        // Registrations these features generate into THIS compilation, for framework types declared in
        // the host project itself. They cannot travel as [assembly: PragmaticMetadata]: the attribute is
        // emitted by this same generator run, and the host reads metadata off its references only. So
        // Composition is registered after them and is handed the entry points directly — the same shape
        // as cacheInvalidationHandlers above. Registration order does not affect execution.
        // Two shapes travel this channel. Most features name a generated Add* for the host to call;
        // Actions, Endpoints and Persistence instead carry the metadata document their own template
        // writes, because the host renders those registrations inline from the models rather than
        // calling anything (see HostLocalRegistration.CreatePayload).
        var localRegistrations = Combine(validationRegistrations, identityRegistrations);
        localRegistrations = Combine(localRegistrations, messagingRegistrations);
        localRegistrations = Combine(localRegistrations, serializationRegistrations);
        localRegistrations = Combine(localRegistrations, temporalRegistrations);
        localRegistrations = Combine(localRegistrations, fastEnumRegistrations);
        localRegistrations = Combine(localRegistrations, redactionRegistrations);
        localRegistrations = Combine(localRegistrations, resilienceRegistrations);
        localRegistrations = Combine(localRegistrations, featureFlagRegistrations);
        localRegistrations = Combine(localRegistrations, pdxTemplateRegistrations);
        localRegistrations = Combine(localRegistrations, rollUpRegistrations);
        localRegistrations = Combine(localRegistrations, readContractRegistrations);
        localRegistrations = Combine(localRegistrations, jobsRegistrations);
        localRegistrations = Combine(localRegistrations, actionsRegistrations);
        localRegistrations = Combine(localRegistrations, derivedPermissionRegistrations);
        localRegistrations = Combine(localRegistrations, endpointRegistrations);
        localRegistrations = Combine(localRegistrations, persistenceRegistrations);
        localRegistrations = Combine(localRegistrations, cachingRegistrations);
        localRegistrations = Combine(localRegistrations, privacyRegistrations);
        localRegistrations = Combine(localRegistrations, configurationRegistrations);

        // The persisted stores go the same way: the host names Add{Boundary}DbContext, {Db}Schema and
        // {Db}MigrationDbContext, which Persistence writes into this compilation only for entities.
        // The [PragmaticUser] resolvers Identity writes are services of the module like any [Service], and
        // the same channel registers them: described, because no attribute scan can find them.
        // …and so are the local identity stores Persistence writes for a user entity that owns a LocalIdentity.
        var localIdentityStores = LocalIdentityStoreFeature.Register(context, entityModels, features);
        var generatedServices = currentUsers.Select(static (users, _) => IdentityFeature.ResolverServices(users))
            .Combine(localIdentityStores)
            .Select(static (pair, _) => new EquatableArray<Features.Composition.Models.ServiceModel>(
                pair.Left.AsImmutableArray().AddRange(pair.Right.AsImmutableArray())));

        // The jobs go on the other channel: JobsFeature registers each of them itself, and what was
        // missing was anyone checking what they take — a missing registration surfaced on a schedule,
        // in a background worker, as a log line.
        CompositionFeature.Register(context, features, persistedStores,
            generatedEventHandlers: cacheInvalidationHandlers,
            localRegistrations: localRegistrations,
            localDomainModules: localDomainModules,
            generatedServices: generatedServices,
            servicesToValidate: jobServices);

        LifecycleEventsFeature.Register(context);
        ValueObjectFeature.Register(context);

        // [LoggerMessage] call sites bound to the Pragmatic attribute. Standalone: the attribute is the
        // whole activation condition, and nothing else in the run depends on the bodies it writes.
        Features.Logging.LogCallSiteFeature.Register(context);

        // A declared Specification<TEntity> reaches the two surfaces it is consumed through. Standalone
        // and guarded by its own symbol lookup: no ordering relationship with anything above.
        SpecificationFeature.Register(context);
        Features.Glossary.GlossaryFeature.Register(context);
        Features.Glossary.ArchitectureFeature.Register(context);
        Features.Glossary.AsyncApiFeature.Register(context);
        Features.Glossary.UseCaseCatalogFeature.Register(context);
    }

    private static IncrementalValueProvider<EquatableArray<Features.Composition.Models.MetadataEntry>> Combine(
        IncrementalValueProvider<EquatableArray<Features.Composition.Models.MetadataEntry>> left,
        IncrementalValueProvider<EquatableArray<Features.Composition.Models.MetadataEntry>> right)
        => left.Combine(right).Select(static (pair, _) =>
            new EquatableArray<Features.Composition.Models.MetadataEntry>(
                pair.Left.AsImmutableArray().AddRange(pair.Right.AsImmutableArray())));
}
