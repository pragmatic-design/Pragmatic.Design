// Pragmatic.Composition.SourceGenerator - Host Mode Generator

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;
using Pragmatic.SourceGenerator.Features.Composition.Validation;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Generators;

/// <summary>
///     Generates code for HOST mode (Exe projects with entry point).
///     Handles aggregation of metadata from referenced assemblies and local services.
/// </summary>
internal static partial class HostModeGenerator
{
    private const string GlobalPrefix = "global::";

    /// <summary>
    ///     Appends the host's own discoveries to those read from references: deduplicated by fully
    ///     qualified name, then ordered by it.
    /// </summary>
    /// <remarks>
    ///     Both properties are load-bearing. A type can reach the host twice — the same assembly is a
    ///     reference of itself in some build graphs, and a trait contributes models the syntax scan also
    ///     sees — and a duplicate here becomes a duplicate DI registration or a route mapped twice.
    ///     The ordering keeps the generated file stable: the extraction order of two different sources
    ///     is not something a snapshot should depend on.
    /// </remarks>
    private static ImmutableArray<T> Merge<T>(
        ImmutableArray<T> fromReferences,
        ImmutableArray<T> fromHost,
        Func<T, string> keySelector)
    {
        if (fromHost.IsDefaultOrEmpty)
            return fromReferences;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = ImmutableArray.CreateBuilder<T>();

        foreach (var item in fromReferences.IsDefaultOrEmpty ? ImmutableArray<T>.Empty : fromReferences)
            if (seen.Add(keySelector(item)))
                merged.Add(item);

        foreach (var item in fromHost)
            if (seen.Add(keySelector(item)))
                merged.Add(item);

        return merged.OrderBy(keySelector, StringComparer.Ordinal).ToImmutableArray();
    }

    public static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<StartupModel> localStartups,
        ImmutableArray<ServiceModel> localServices,
        ImmutableArray<DecoratorModel> localDecorators,
        ImmutableArray<ModuleModel> localModules,
        Persistence.Models.PersistedStoresModel persistedStores,
        Core.DetectedFeatures? detectedFeatures = null,
        ImmutableArray<AssemblyMetadataModel> assemblyMetadata = default,
        ImmutableArray<DiscoveredModuleInfo> domainModules = default,
        ImmutableArray<MetadataEntry> localRegistrations = default,
        ImmutableArray<DiscoveredModuleInfo> localDomainModules = default)
    {
        // Use pre-computed metadata if available, otherwise read from references (backward compat)
        if (assemblyMetadata.IsDefault)
            assemblyMetadata = MetadataReader.ReadFromReferences(compilation, context.CancellationToken);
        if (domainModules.IsDefault)
            domainModules = MetadataReader.ReadDomainModulesFromReferences(compilation, context.CancellationToken);

        // Framework types declared in the host project itself. Their registrations are generated into
        // this very compilation, so no [assembly: PragmaticMetadata] can be read back for them and the
        // features that render them hand their entries over directly (see HostLocalRegistration).
        // Held aside rather than merged into assemblyMetadata here: this assembly is the host, so it
        // must not go through the [Include<T>] filter — "a module the host chose not to include" is not
        // a thing it can be — nor be counted as one of the discovered modules.
        var localMetadata = localRegistrations.IsDefaultOrEmpty
            ? null
            : new AssemblyMetadataModel
            {
                AssemblyName = compilation.AssemblyName ?? "PragmaticHost",
                Entries = localRegistrations
            };

        var localMetadataOnly = localMetadata is null
            ? ImmutableArray<AssemblyMetadataModel>.Empty
            : ImmutableArray.Create(localMetadata);

        // Extract module information from metadata
        var discoveredModules = MetadataReader.ExtractModules(assemblyMetadata);

        // Extract enriched endpoint registrations from Endpoints metadata
        var (discoveredEndpoints, discoveredEndpointGroups, hasAspVersioning) =
            MetadataReader.ExtractEndpointRegistrations(assemblyMetadata);

        // Collect package route prefixes from local + discovered modules
        var packageRoutePrefixMap = BuildPackageRoutePrefixMap(localModules, discoveredModules);

        // Tag package endpoints with their route prefix for MapGroup generation
        if (packageRoutePrefixMap.Count > 0)
        {
            discoveredEndpoints = discoveredEndpoints
                .Select(ep =>
                {
                    if (packageRoutePrefixMap.TryGetValue(ep.SourceAssembly, out var prefix))
                        return ep with { PackageRoutePrefix = prefix };
                    return ep;
                })
                .ToImmutableArray();
        }

        ReportDuplicateRoutes(context, discoveredEndpoints, discoveredEndpointGroups);
        ReportRoutesNobodyCanCall(context, discoveredEndpoints, localModules);

        // Check if any DI (services/decorators) entries were discovered from referenced assemblies
        var hasDiEntries = assemblyMetadata.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.DI));
        if (!hasDiEntries)
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.NoServicesDiscovered,
                Location.None));

        // Check if any Startup (pipeline step) entries were discovered from referenced assemblies
        var hasStartupEntries = assemblyMetadata.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Startup));
        if (!hasStartupEntries)
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.NoPipelineStepsDiscovered,
                Location.None));

        // Report discovery info
        if (assemblyMetadata.Length > 0)
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.DiscoveredModules,
                Location.None,
                assemblyMetadata.Length));

        // Report discovered modules count
        if (discoveredModules.Length > 0)
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.DiscoveredModulesInfo,
                Location.None,
                discoveredModules.Length,
                string.Join(", ", discoveredModules.Select(m => m.Name))));

        // Report discovered Domain modules
        if (domainModules.Length > 0)
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.DiscoveredModulesInfo,
                Location.None,
                domainModules.Length,
                $"Domain: {string.Join(", ", domainModules.Select(m => m.Name))}"));

        // Collect [Include<T>] host wiring declarations from local [Module] classes, with the migration
        // context of their database when Persistence writes one (for database initialization)
        var hostIncludes = localModules.IsDefaultOrEmpty
            ? ImmutableArray<HostIncludeModel>.Empty
            : localModules
                .SelectMany(m => m.HostIncludes)
                .Select(i => EnrichWithMigrationDbContext(i, persistedStores))
                .ToImmutableArray();

        // Aggregate [NeedsStep<T>] from all local modules (deduplicated)
        var aggregatedNeedsSteps = localModules.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : localModules
                .Where(m => !m.NeedsSteps.IsDefaultOrEmpty)
                .SelectMany(m => m.NeedsSteps)
                .Distinct()
                .ToImmutableArray();

        // Validate that all NeedsStep types exist in the compilation
        if (!aggregatedNeedsSteps.IsDefaultOrEmpty)
        {
            foreach (var stepFqn in aggregatedNeedsSteps)
            {
                // Strip "global::" prefix for GetTypeByMetadataName
                var metadataName = stepFqn.StartsWith(GlobalPrefix, StringComparison.Ordinal)
                    ? stepFqn.Substring(GlobalPrefix.Length)
                    : stepFqn;

                var stepType = compilation.GetTypeByMetadataName(metadataName);
                if (stepType is null)
                {
                    // Find which module declared this step
                    var declaringModule = localModules.FirstOrDefault(m =>
                        !m.NeedsSteps.IsDefaultOrEmpty && m.NeedsSteps.Contains(stepFqn));

                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.NeedsStepTypeNotFound,
                        declaringModule?.Location ?? Location.None,
                        declaringModule?.Name ?? "Unknown",
                        metadataName));
                }
            }
        }

        // Validate database topology (PRAG1651, PRAG1652, PRAG1607, PRAG1608)
        if (!hostIncludes.IsDefaultOrEmpty)
            DatabaseTopologyValidator.Validate(context, hostIncludes, domainModules);

        // Validate module dependencies ([IncludeModule<T>] — PRAG1601 unresolved, PRAG1602 cycle)
        Validation.ModuleDependencyValidator.Validate(context, localModules, discoveredModules, domainModules);

        // Collect [RemoteBoundary<T>] declarations from local modules
        var remoteBoundaries = localModules.IsDefaultOrEmpty
            ? ImmutableArray<RemoteBoundaryModel>.Empty
            : localModules
                .SelectMany(m => m.RemoteBoundaries)
                .ToImmutableArray();

        // Validate: no overlap between Include and RemoteBoundary
        if (!remoteBoundaries.IsDefaultOrEmpty && !hostIncludes.IsDefaultOrEmpty)
        {
            var includedModules = new HashSet<string>(
                hostIncludes.Select(i => i.ModuleTypeName), StringComparer.Ordinal);

            foreach (var rb in remoteBoundaries)
            {
                if (includedModules.Contains(rb.ModuleTypeName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.RemoteBoundaryOverlapsInclude,
                        rb.Location ?? Location.None,
                        rb.ModuleName));
                }
            }
        }

        // Collect exposed endpoints from local modules + discovered modules
        var exposedEndpoints = CollectExposedEndpoints(localModules, discoveredModules);

        // Which modules this host hosts, before any package is folded in — a package reaches a host
        // through a module the host hosts, so the two cannot be worked out in one pass.
        var includedModuleAssemblies = BuildIncludedModuleAssemblies(
            hostIncludes, localModules, domainModules, remoteBoundaries, compilation);

        // PRAG1603 — against the same set the registrations are filtered by, so the check and what the
        // host actually registers cannot disagree.
        Validation.HostedDependencyValidator.Validate(
            context, includedModuleAssemblies, localModules, discoveredModules, remoteBoundaries);

        // Collect [UsePackage<T>] declarations from local modules AND the discovered modules this host
        // actually hosts.
        var packageAssemblyNames =
            CollectAndValidatePackages(context, localModules, discoveredModules, includedModuleAssemblies);

        // Split assembly metadata: non-package vs package assemblies.
        // Package assemblies are only included when explicitly imported via [UsePackage<T>].
        // This prevents double-registration when a package is transitively referenced.
        var nonPackageMetadata = packageAssemblyNames.IsDefaultOrEmpty
            ? assemblyMetadata
            : assemblyMetadata.Where(a => !packageAssemblyNames.Contains(a.AssemblyName)).ToImmutableArray();

        var packageMetadata = packageAssemblyNames.IsDefaultOrEmpty
            ? ImmutableArray<AssemblyMetadataModel>.Empty
            : assemblyMetadata.Where(a => packageAssemblyNames.Contains(a.AssemblyName)).ToImmutableArray();

        // Extract from non-package assemblies (normal modules)
        var (discoveredServices, discoveredDecorators) = MetadataReader.ExtractServiceRegistrations(nonPackageMetadata);
        var (discoveredActions, discoveredMutations) = MetadataReader.ExtractActionRegistrations(nonPackageMetadata);
        var discoveredRepositories = MetadataReader.ExtractRepositoryRegistrations(nonPackageMetadata);

        // Fuse package metadata (from [UsePackage<T>] declarations on modules)
        if (packageMetadata.Length > 0)
        {
            var (pkgServices, pkgDecorators) = MetadataReader.ExtractServiceRegistrations(packageMetadata);
            var (pkgActions, pkgMutations) = MetadataReader.ExtractActionRegistrations(packageMetadata);
            var pkgRepositories = MetadataReader.ExtractRepositoryRegistrations(packageMetadata);

            discoveredServices = discoveredServices.AddRange(pkgServices);
            discoveredDecorators = discoveredDecorators.AddRange(pkgDecorators);
            discoveredActions = discoveredActions.AddRange(pkgActions);
            discoveredMutations = discoveredMutations.AddRange(pkgMutations);
            discoveredRepositories = discoveredRepositories.AddRange(pkgRepositories);
        }

        // Filter discovered entries to only included assemblies (standalone host scenario).
        // In a standalone host with [Include<BillingModule>], we should NOT register services
        // from Catalog/Booking that are transitively referenced but not included.
        var includedAssemblyNames = BuildIncludedAssemblyNames(
            hostIncludes, localModules, domainModules, packageAssemblyNames, remoteBoundaries, compilation);

        if (includedAssemblyNames is not null)
        {
            discoveredServices = discoveredServices
                .Where(s => includedAssemblyNames.Contains(s.SourceAssembly)).ToImmutableArray();
            discoveredDecorators = discoveredDecorators
                .Where(d => includedAssemblyNames.Contains(d.SourceAssembly)).ToImmutableArray();
            discoveredActions = discoveredActions
                .Where(a => includedAssemblyNames.Contains(a.SourceAssembly)).ToImmutableArray();
            discoveredMutations = discoveredMutations
                .Where(m => includedAssemblyNames.Contains(m.SourceAssembly)).ToImmutableArray();
            discoveredRepositories = discoveredRepositories
                .Where(r => includedAssemblyNames.Contains(r.SourceAssembly)).ToImmutableArray();
            discoveredEndpoints = discoveredEndpoints
                .Where(e => includedAssemblyNames.Contains(e.SourceAssembly)).ToImmutableArray();
            discoveredEndpointGroups = discoveredEndpointGroups
                .Where(g => includedAssemblyNames.Contains(g.SourceAssembly)).ToImmutableArray();

            // ⚠️ Read the messaging registrations this filter is about to drop, and say so. An assembly
            // with no [Module] is not discovered, so a contracts project — which must not be a module,
            // or a consumer referencing it drags the module in — has its generated
            // AddPragmaticMessageHandlers filtered out with everything else it declares, and nothing at
            // build time says a word. On Casework that was six red integration tests and no signal.
            // Whether discovery should follow the metadata rather than the module is a
            // decision about every host and is not taken here.
            ReportUncalledMessagingRegistrations(context, compilation, assemblyMetadata, includedAssemblyNames);

            // Filter assembly metadata for model (validators, event handlers, etc.)
            assemblyMetadata = assemblyMetadata
                .Where(a => includedAssemblyNames.Contains(a.AssemblyName)).ToImmutableArray();
        }

        // The routes of a module reached over HTTP are served by the host that owns it.
        //
        // The filter above deliberately keeps a [RemoteBoundary<T>] assembly — its actions are what the
        // HTTP invokers are built from — so without this its endpoints survive and get mapped, while
        // DomainActions excludes their invokers just as deliberately. That combination would publish
        // routes answering 500 because the invoker the generated endpoint injects is not registered —
        // six Billing routes on Showcase.Host.Distributed. [RemoteBoundary<T>] is a client
        // declaration, not a gateway one: reaching the module means injecting I{Boundary}Actions, and
        // one address in front of two services is a reverse proxy.
        var remoteAssemblies = remoteBoundaries.IsDefaultOrEmpty
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                remoteBoundaries.Where(rb => rb.AssemblyName is not null).Select(rb => rb.AssemblyName!),
                StringComparer.Ordinal);

        if (remoteAssemblies.Count > 0)
        {
            discoveredEndpoints = discoveredEndpoints
                .Where(e => !remoteAssemblies.Contains(e.SourceAssembly)).ToImmutableArray();
            discoveredEndpointGroups = discoveredEndpointGroups
                .Where(g => !remoteAssemblies.Contains(g.SourceAssembly)).ToImmutableArray();

            // ⚠️ And its repositories, for a reason the routes made visible only once the host was
            // started. A repository takes the boundary's DbContext, and a DbContext is
            // registered from the host's [Include<TModule, TDatabase>] — which a remote module has
            // none of, because naming a database for it is exactly what declaring it remote says it
            // does not do. So they were registered and unbuildable: the first thing the distributed
            // host's container validation reported, the moment anything started it.
            discoveredRepositories = discoveredRepositories
                .Where(r => !remoteAssemblies.Contains(r.SourceAssembly)).ToImmutableArray();
        }

        // An [ExposeEndpoint<TAction>] belongs to the host that hosts the module declaring it.
        //
        // ⚠️ This list is the one exception that had to be filtered here rather than above:
        // CollectExposedEndpoints runs before BuildIncludedAssemblyNames, so these endpoints reached the
        // host untouched by *either* rule — not the include filter, and not the remote one. The handler
        // is generated into the host's own namespace, which is why reading a generated host for the
        // declaring module's name shows nothing and the gap went unseen.
        //
        // The route would answer 500: the generated handler injects the
        // action's invoker, and a module this host does not host has none registered.
        if (!exposedEndpoints.IsDefaultOrEmpty)
        {
            exposedEndpoints = exposedEndpoints
                .Where(e => !remoteAssemblies.Contains(e.ActionAssemblyName))
                .Where(e => includedAssemblyNames is null
                            || includedAssemblyNames.Contains(e.ActionAssemblyName))
                .ToImmutableArray();
        }

        // The host's own declarations, read with the same parsers as a referenced assembly's — the
        // payload is the same document its metadata template writes. Merged after the [Include<T>]
        // filter and after the package split, which are both about *other* assemblies.
        if (localMetadata is not null)
        {
            var (localActions, localMutations) = MetadataReader.ExtractActionRegistrations(localMetadataOnly);
            discoveredActions = Merge(discoveredActions, localActions, a => a.ActionType);
            discoveredMutations = Merge(discoveredMutations, localMutations, m => m.MutationType);

            discoveredRepositories = Merge(discoveredRepositories,
                MetadataReader.ExtractRepositoryRegistrations(localMetadataOnly), r => r.EntityType);

            var (localEndpoints, localGroups, localHasAspVersioning) =
                MetadataReader.ExtractEndpointRegistrations(localMetadataOnly);
            discoveredEndpoints = Merge(discoveredEndpoints, localEndpoints, e => e.EndpointType);
            discoveredEndpointGroups = Merge(discoveredEndpointGroups, localGroups, g => g.GroupType);
            hasAspVersioning |= localHasAspVersioning;
        }

        // The groups exposed endpoints name, which no endpoint metadata lists.
        discoveredEndpointGroups = AddExposedEndpointGroups(context, compilation, exposedEndpoints, discoveredEndpointGroups);

        // Report remote boundary diagnostics (no actions, no URL)
        if (!remoteBoundaries.IsDefaultOrEmpty)
        {
            var actionAssemblies = new HashSet<string>(StringComparer.Ordinal);
            if (!discoveredActions.IsDefaultOrEmpty)
                foreach (var a in discoveredActions)
                    actionAssemblies.Add(a.SourceAssembly);

            foreach (var rb in remoteBoundaries)
            {
                if (rb.AssemblyName is not null && !actionAssemblies.Contains(rb.AssemblyName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.RemoteBoundaryNoActions,
                        rb.Location ?? Location.None,
                        rb.ModuleName));
                }

                // A compensator declared on an action that this host reaches over HTTP: the attribute
                // is on the callee's side of the wire, so nothing here will ever call it.
                if (rb.AssemblyName is not null)
                {
                    foreach (var compensable in CompensableTypesIn(discoveredActions, discoveredMutations, rb.AssemblyName))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            CompositionDiagnostics.RemoteBoundaryCompensationUnreachable,
                            rb.Location ?? Location.None,
                            rb.ModuleName, compensable));
                    }
                }

                if (rb.BaseUrl is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.RemoteBoundaryNoBaseUrl,
                        rb.Location ?? Location.None,
                        rb.ModuleName));
                }
            }
        }

        // Generate aggregated API manifest at host level (merges all module manifests).
        // The host's own manifest is passed alongside rather than merged into assemblyMetadata: that
        // merge happens further down, after schema validation, and moving it up would put a document
        // this run just wrote through a compatibility check against itself.
        // ⚠️ Minus the remote assemblies, for the same reason their routes are not mapped: a host that
        // publishes a path it does not serve is the same defect one layer up, in the document a
        // client generates from.
        GenerateAggregatedManifest(
            context,
            remoteAssemblies.Count == 0
                ? assemblyMetadata
                : assemblyMetadata.Where(a => !remoteAssemblies.Contains(a.AssemblyName)).ToImmutableArray(),
            localMetadataOnly,
            compilation);

        // Extract required configuration sections from [RequiresConfig] declarations.
        // Combines sections from:
        // 1. Referenced assemblies (read via StartupMetadataTemplate JSON)
        // 2. Local startups in this host project (read directly from StartupModel)
        var fromReferences = MetadataReader.ExtractRequiredConfigSections(assemblyMetadata);
        var fromLocalStartups = localStartups.IsDefaultOrEmpty
            ? ImmutableArray<RequiredConfigSectionInfo>.Empty
            : localStartups
                .Where(s => !s.RequiredConfigSections.IsDefaultOrEmpty)
                .SelectMany(s => s.RequiredConfigSections
                    .Select(sp => new RequiredConfigSectionInfo
                    {
                        SectionPath = sp,
                        SourceModuleType = s.FullTypeName
                    }))
                .ToImmutableArray();
        var requiredConfigSections = fromLocalStartups.AddRange(fromReferences);

        // Validate schema versions
        var validationErrors = ValidateSchemaVersions(assemblyMetadata);

        // Report validation errors
        foreach (var error in validationErrors)
        {
            var descriptor = CompositionDiagnostics.SchemaDescriptor(error.DiagnosticId);
            if (descriptor is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    Location.None,
                    error.MessageArgs.AsImmutableArray().ToArray<object?>()));
        }

        // If there are errors, don't generate
        if (validationErrors.Any(e => e.DiagnosticId == "PRAG1610"))
            // PRAG1610 is a breaking incompatibility — skip generation
            return;

        // The host's own entries also reach the aggregation model, for the call sites that dispatch on
        // an entry's registration method (validators, jobs, message handlers, …). Added after schema
        // validation on purpose: these documents are written by this generator run, so their schema
        // version is the current one by construction and there is nothing to be incompatible with.
        if (localMetadata is not null)
            assemblyMetadata = assemblyMetadata.Add(localMetadata);

        // Get root namespace
        var rootNamespace = GetRootNamespace(compilation);

        // Generate aggregated registration code
        var isDebug = compilation.Options.OptimizationLevel == OptimizationLevel.Debug;

        var model = new HostAggregationModel
        {
            Assemblies = assemblyMetadata,
            RootNamespace = rootNamespace,
            GenerateTopologyReport = isDebug,
            ValidationErrors = validationErrors.ToImmutableArray(),
            DiscoveredModules = discoveredModules,
            LocalModules = localModules,
            // Ordered so the generated output does not depend on the set's enumeration order.
            IncludedAssemblyNames = includedAssemblyNames is null
                ? ImmutableArray<string>.Empty
                : includedAssemblyNames.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray(),
            DomainModules = domainModules,
            LocalDomainModules = localDomainModules.IsDefault
                ? ImmutableArray<DiscoveredModuleInfo>.Empty
                : localDomainModules,
            DiscoveredEndpoints = discoveredEndpoints,
            DiscoveredEndpointGroups = discoveredEndpointGroups,
            HasAspVersioning = hasAspVersioning,
            HostIncludes = hostIncludes,
            PersistedStores = persistedStores,
            DiscoveredServices = discoveredServices,
            DiscoveredDecorators = discoveredDecorators,
            DiscoveredActions = discoveredActions,
            DiscoveredMutations = discoveredMutations,
            DiscoveredRepositories = discoveredRepositories,
            // Read off assemblyMetadata, which by here is already narrowed to the included assemblies,
            // plus the host's own document — the same two sources the query-filter registration uses.
            DiscoveredLookupRegistrations = MetadataReader
                .ExtractLookupRegistrations(assemblyMetadata)
                .AddRange(localMetadata is null
                    ? ImmutableArray<string>.Empty
                    : MetadataReader.ExtractLookupRegistrations(localMetadataOnly))
                .Distinct()
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToImmutableArray(),
            LocalServices = localServices,
            LocalDecorators = localDecorators,
            RequiredConfigSections = requiredConfigSections,
            AggregatedNeedsSteps = aggregatedNeedsSteps,
            HasPersistenceSerialization = compilation.GetTypeByMetadataName("Pragmatic.Persistence.Serialization.EntityJsonModifier") is not null,
            // The same probe GenerateOpenApiDocument makes, and the same answer: with the registry
            // referenced a document is always emitted, so this is "the host has one to register".
            HasOpenApiDocument = compilation.GetTypeByMetadataName(
                "Pragmatic.Endpoints.OpenApi.PragmaticOpenApiRegistry") is not null,
            // The same probe GenerateAggregatedManifest makes before it emits PragmaticManifest, plus
            // the type the registration names: a host may reference Pragmatic.Endpoints (the registry)
            // without Pragmatic.Endpoints.OpenApi (where HostManifest lives).
            HasAggregatedManifest = compilation.GetTypeByMetadataName(
                    "Pragmatic.Endpoints.Manifest.ManifestRegistry") is not null
                && compilation.GetTypeByMetadataName(
                    "Pragmatic.Endpoints.OpenApi.HostManifest") is not null,
            DetectedFeatures = detectedFeatures ?? Core.DetectedFeatures.None,
            DiscoveredCacheCategories = MetadataReader.ExtractCacheCategories(assemblyMetadata),
            DeclaredLanguages = MetadataReader.ExtractDeclaredLanguages(assemblyMetadata),
            TranslationProviders = MetadataReader.ExtractTranslationProviders(assemblyMetadata),
            PdxTemplateAnchors = MetadataReader.ExtractPdxTemplateAnchors(assemblyMetadata),
            PackageAssemblyNames = packageAssemblyNames,
            ExposedEndpoints = exposedEndpoints,
            RemoteBoundaries = remoteBoundaries
        };

        // Event handler registration is now handled by a separate pipeline in CompositionFeature
        // to avoid regenerating host output when only event handlers change.

        // Generate PragmaticHost.g.cs (internal helper class)
        var hostTemplate = new PragmaticHostTemplate(model, localStartups);
        var hostArtifact = hostTemplate.RenderOutput();
        context.AddSource(hostArtifact);

        // Emit the zero-reflection HostTopology provider so FromRegistry() works at runtime (AOT-safe).
        var topologyJson = hostTemplate.BuildTopologyJsonOrNull();
        if (topologyJson is not null)
            GenerateHostTopologyMetadataProvider(context, rootNamespace, topologyJson);

        // Emit the on-demand migration coordinator (FU2) so MigrateCommand can re-run migrations.
        if ((detectedFeatures ?? Core.DetectedFeatures.None).HasMigrations)
            GenerateMigrationCoordinator(context, model, rootNamespace);

        // Generate Pragmatic.g.cs (public entry point)
        var entryTemplate = new PragmaticEntryTemplate(model);
        var entryArtifact = entryTemplate.RenderOutput();
        context.AddSource(entryArtifact);

        // Generate exposed endpoint handlers (from [ExposeEndpoint<T>])
        if (!exposedEndpoints.IsDefaultOrEmpty)
            GenerateExposedEndpointHandlers(context, compilation, exposedEndpoints, packageRoutePrefixMap, packageAssemblyNames, rootNamespace,
                (detectedFeatures ?? Core.DetectedFeatures.None).HasIdentityAspNetCore);

        // HTTP invokers for remote boundary actions are not generated individually.
        // Remote boundaries use AddXxxBoundary(BoundaryMode.Remote) which delegates to
        // the module's RemoteActions class (generated in _Boundary.Xxx.Remote.g.cs).

        // Generate /_pragmatic/invoke dispatcher for local actions
        // (any host with local actions can serve as a remote boundary target)
        if (!discoveredActions.IsDefaultOrEmpty || !discoveredMutations.IsDefaultOrEmpty)
        {
            var localActions = GetNonRemoteActions(discoveredActions, remoteBoundaries);
            if (localActions.Length > 0)
            {
                var dispatcherTemplate = new PragmaticInvokeEndpointTemplate(localActions, rootNamespace);
                var dispatcherArtifact = dispatcherTemplate.RenderOutput();
                context.AddSource(dispatcherArtifact);
            }
        }

        // Generate PragmaticTopology.g.cs (Debug only)
        if (isDebug)
        {
            var topologyTemplate = new TopologyReportTemplate(model);
            var topologyArtifact = topologyTemplate.RenderOutput();
            context.AddSource(topologyArtifact);
        }
    }


    /// <summary>Names, simple, of the module's actions and mutations that declare a compensator.</summary>
    private static IEnumerable<string> CompensableTypesIn(
        ImmutableArray<Models.DiscoveredActionInfo> actions,
        ImmutableArray<Models.DiscoveredMutationInfo> mutations,
        string assemblyName)
    {
        if (!actions.IsDefaultOrEmpty)
            foreach (var a in actions)
                if (a.SourceAssembly == assemblyName && a.CompensatorType is not null)
                    yield return SimpleName(a.ActionType);

        if (!mutations.IsDefaultOrEmpty)
            foreach (var m in mutations)
                if (m.SourceAssembly == assemblyName && m.CompensatorType is not null)
                    yield return SimpleName(m.MutationType);

        static string SimpleName(string fullName)
        {
            var cut = fullName.LastIndexOf('.');
            return cut < 0 ? fullName : fullName.Substring(cut + 1);
        }
    }

    /// <summary>
    ///     Two operations published at the same address — <c>PRAG1697</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only the host can see it: operations live in different libraries, and one compilation
    ///         sees one of them. It is the check that «two identical mutations» was reaching for, with
    ///         the definition corrected — the payload is the identity of the row, the route is the
    ///         address, and only an address can collide.
    ///     </para>
    ///     <para>
    ///         The comparison uses the package prefix plus the declared route, because two endpoints
    ///         that share a route and differ by prefix are two different addresses. The verb is part
    ///         of it: <c>GET /orders/{id}</c> and <c>DELETE /orders/{id}</c> are not in conflict.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     PRAG1692: an <c>[AnonymousHost]</c> that discovered a route enforcing a permission its author
    ///     never declared, and therefore one no caller of this host can ever hold.
    /// </summary>
    /// <remarks>
    ///     Here because here both facts are known: the host says whether it authenticates, and the
    ///     discovered routes say which of them gate on a derived permission. The module is not asked: it
    ///     could only answer with what it references, which would pin <c>Identity.AspNetCore</c> to
    ///     boundary libraries that need nothing from it.
    /// </remarks>
    private static void ReportRoutesNobodyCanCall(
        SourceProductionContext context,
        ImmutableArray<DiscoveredEndpointRouteInfo> endpoints,
        ImmutableArray<ModuleModel> localModules)
    {
        if (localModules.IsDefaultOrEmpty || !localModules.Any(m => m.IsAnonymousHost))
            return;

        foreach (var endpoint in endpoints)
        {
            if (endpoint.DerivedPermission is not { Length: > 0 } permission)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.AnonymousHostCannotHoldADerivedPermission,
                Location.None,
                endpoint.Route ?? endpoint.EndpointType,
                permission));
        }
    }

    private static void ReportDuplicateRoutes(
        SourceProductionContext context,
        ImmutableArray<DiscoveredEndpointRouteInfo> endpoints,
        ImmutableArray<DiscoveredEndpointGroupInfo> groups)
    {
        // ⚠️ The group's prefix is part of the address, and leaving it out is not a detail: in the
        // reference application a dozen endpoints are `GET /{id}` and differ only by the group they
        // hang from. The first version of this check reported every one of them, and the Showcase
        // said so at the first build — the same consumer that falsified the payload-based check
        // this one replaces.
        var prefixByGroup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
            prefixByGroup[group.GroupType] = group.RoutePrefix;

        var seen = new Dictionary<string, (string Type, string Address)>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in endpoints)
        {
            if (endpoint.Verb is not { Length: > 0 } verb || endpoint.Route is not { Length: > 0 } route)
                continue;

            var groupPrefix = endpoint.GroupType is { Length: > 0 } groupType
                              && prefixByGroup.TryGetValue(groupType, out var resolved)
                ? resolved
                : "";

            var path = $"{endpoint.PackageRoutePrefix}{groupPrefix}{route}";
            var address = $"{verb} {path}";

            if (seen.TryGetValue(address, out var first))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.DuplicateEndpointRoute,
                    Location.None,
                    first.Type, endpoint.EndpointType, verb, path));
                continue;
            }

            seen[address] = (endpoint.EndpointType, address);
        }
    }

}
