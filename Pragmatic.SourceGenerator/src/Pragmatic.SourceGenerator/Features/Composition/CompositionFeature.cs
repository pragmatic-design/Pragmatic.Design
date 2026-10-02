// Pragmatic.SourceGenerator - Composition Feature

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Generators;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;
using Pragmatic.SourceGenerator.Features.Composition.Validation;
using Pragmatic.SourceGenerator.Features.Messaging.Diagnostics;

namespace Pragmatic.SourceGenerator.Features.Composition;

/// <summary>
///     Composition feature: registers all pipeline stages for service registration ([Service], [Decorator]),
///     startup steps ([StartupStep]), module definitions ([Module]),
///     event handlers ([EventHandler]), and host-level aggregation.
///     Activated when Pragmatic.Composition runtime is referenced.
///     Uses cached providers for metadata reads to avoid redundant assembly scanning.
/// </summary>
internal static class CompositionFeature
{
    // Startup step attribute
    private const string StartupStepAttributeFullName = "Pragmatic.Composition.Attributes.StartupStepAttribute";

    private const string ServiceAttributeFullName = "Pragmatic.Composition.Attributes.ServiceAttribute";
    private const string ServiceGenericAttributeFullName = "Pragmatic.Composition.Attributes.ServiceAttribute`1";
    private const string DecoratorAttributeFullName = "Pragmatic.Composition.Attributes.DecoratorAttribute";
    private const string ModuleAttributeFullName = "Pragmatic.Composition.Attributes.ModuleAttribute";
    private const string EventHandlerAttributeFullName = "Pragmatic.Events.Attributes.EventHandlerAttribute";

    /// <param name="context">The generator's initialization context.</param>
    /// <param name="features">Which Pragmatic packages this compilation references.</param>
    /// <param name="persistedStores">The stores Persistence wrote into this compilation.</param>
    /// <param name="generatedEventHandlers">Handlers another feature writes, to be registered here.</param>
    /// <param name="localRegistrations">What this compilation generates for itself, for a host that declares its own.</param>
    /// <param name="localDomainModules">The boundaries of this compilation, which no assembly attribute carries back.</param>
    /// <param name="generatedServices">
    ///     Services another feature writes and this one must <b>register</b> — the <c>[PragmaticUser]</c>
    ///     resolver, the local identity store. Described rather than scanned for, because the types do
    ///     not exist in the compilation being analysed.
    /// </param>
    /// <param name="servicesToValidate">
    ///     Classes the container resolves that somebody else registers, described so the dependency
    ///     validator can see what they take — a job, whose own feature writes
    ///     <c>TryAddScoped&lt;TJob&gt;()</c>.
    ///     <para>
    ///         ⚠️ A separate channel on purpose: these must <b>not</b> reach the registration generator.
    ///         Handing a job through <c>generatedServices</c> made <c>_Infra.DI.ServiceRegistration.g.cs</c>
    ///         register it a second time — harmless at run time because both are <c>TryAdd</c>, and
    ///         exactly the "one truth, two producers" shape this repository keeps paying for. The
    ///         snapshots of the Jobs suite caught it.
    ///     </para>
    /// </param>
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<Persistence.Models.PersistedStoresModel> persistedStores,
        IncrementalValueProvider<ImmutableArray<EventHandlerModel>>? generatedEventHandlers = null,
        IncrementalValueProvider<EquatableArray<MetadataEntry>>? localRegistrations = null,
        IncrementalValueProvider<EquatableArray<DiscoveredModuleInfo>>? localDomainModules = null,
        IncrementalValueProvider<EquatableArray<ServiceModel>>? generatedServices = null,
        IncrementalValueProvider<EquatableArray<ServiceModel>>? servicesToValidate = null)
    {
        // Provider for [StartupStep] classes
        var startupStepProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StartupStepAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                StartupTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.CompositionStartupSteps);

        // Provider for [Service] classes (non-generic)
        var serviceProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ServiceAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                ServiceTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Provider for [Service<TInterface>] classes (generic)
        var serviceGenericProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ServiceGenericAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                ServiceTransform.TransformGeneric)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Provider for [Decorator] classes
        var decoratorProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DecoratorAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                DecoratorTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Generate decorator delegation stubs (independent of Library/Host mode)
        context.RegisterSourceOutputSafe(decoratorProvider, static (ctx, decorator) =>
        {
            if (decorator is null) return;
            var template = new Templates.DecoratorDelegationTemplate(decorator);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                ctx.AddSource(artifact);
        });

        // Provider for [ServiceFactory] classes — each [Factory] method registers its return type.
        var serviceFactoryProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Composition.Attributes.ServiceFactoryAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                ServiceFactoryTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // [ServiceFactory] output is registered later (after refMetadataProvider) so the host can also
        // aggregate factories discovered in referenced boundary libraries.

        // Provider for [Module] classes
        var moduleProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ModuleAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                ModuleTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.CompositionModules);

        // Provider for [EventHandler] classes
        var eventHandlerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EventHandlerAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                EventHandlerTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.CompositionEventHandlers);

        // Merge both service providers (generic and non-generic)
        var allServicesProvider = serviceProvider.Collect()
            .Combine(serviceGenericProvider.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right))
            .WithTrackingName(TrackingNames.CompositionServices);

        // Services other features generate (today: the [PragmaticUser] resolver). Same reason as the
        // generated event handlers below: the types do not exist in the compilation being analysed, so
        // no [Service] scan can find them, and whoever writes them has to describe them.
        if (generatedServices is not null)
            allServicesProvider = allServicesProvider.Combine(generatedServices.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right.AsImmutableArray()));

        // Described for the validator and for nobody else: a job is registered by its own feature, so
        // adding it above would register it twice.
        var validationOnly = servicesToValidate
                             ?? context.CompilationProvider.Select(
                                 static (_, _) => EquatableArray<ServiceModel>.Empty);

        // Collected once and shared by every downstream stage — collecting the same source provider
        // twice would create two independent batch nodes doing identical work.
        var allStartupSteps = startupStepProvider.Collect();
        var allDecorators = decoratorProvider.Collect();
        var allModules = moduleProvider.Collect();
        var allEventHandlers = eventHandlerProvider.Collect();

        // Handlers other features generate (today: the [InvalidatesCache] cache-invalidation handler)
        // are merged in here. They cannot arrive through eventHandlerProvider: the types do not exist
        // in the compilation being analysed, so no attribute scan can find them.
        if (generatedEventHandlers is not null)
            allEventHandlers = allEventHandlers.Combine(generatedEventHandlers.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        // ================================================================
        // Cached metadata providers — re-execute only when references change
        // ================================================================

        var refMetadataProvider = context.CompilationProvider
            .Select(static (c, ct) => MetadataReader.ReadFromReferences(c, ct));

        var domainModulesProvider = context.CompilationProvider
            .Select(static (c, ct) => MetadataReader.ReadDomainModulesFromReferences(c, ct));

        // Scalar projection of everything the mode-gated stages need from the Compilation: the
        // generator mode and the root namespace. Combining the raw CompilationProvider instead would
        // re-run those stages on every keystroke, because the Compilation is a new object each edit.
        var modeAndRootNamespace = context.CompilationProvider
            .Select(static (c, _) => (
                Mode: CompositionDetector.DetermineMode(c),
                RootNamespace: c.AssemblyName ?? "PragmaticHost"));

        // [ServiceFactory]: host mode ALWAYS emits PragmaticServiceFactories (empty
        // method if none) so the generated entry can call it unconditionally — registering host-local
        // factories AND those discovered in referenced boundary libraries' DI metadata (cross-assembly).
        // A non-host library with factories emits factory metadata so a consuming host can aggregate them.
        var serviceFactoryChain = serviceFactoryProvider.Collect()
            .Combine(modeAndRootNamespace)
            .Combine(features)
            .Combine(refMetadataProvider);

        context.RegisterSourceOutputSafe(serviceFactoryChain, static (ctx, pair) =>
        {
            var (((factories, compilationInfo), detectedFeatures), refMetadata) = pair;
            if (!detectedFeatures.HasComposition)
                return;

            var localValid = factories.Where(f => f.IsValid).ToImmutableArray();

            if (compilationInfo.Mode == GeneratorMode.Host)
            {
                var all = localValid.AddRange(MetadataReader.ExtractServiceFactories(refMetadata));
                var rootNamespace = compilationInfo.RootNamespace;
                var artifact = new Templates.ServiceFactoryRegistrationTemplate(all, rootNamespace).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }
            else if (localValid.Length > 0)
            {
                // Library/Skip mode with factories: publish metadata for a host to discover.
                var artifact = new Templates.ServiceFactoryMetadataTemplate(localValid, indent: false).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }
        });

        // ================================================================
        // Host mode: event handler registration (independent pipeline)
        // Only depends on eventHandlers + compilation (for rootNamespace).
        // Changes to services/modules don't trigger regeneration.
        // ================================================================

        var eventHandlerChain = allEventHandlers
            .Combine(modeAndRootNamespace)
            .Combine(features);

        context.RegisterSourceOutputSafe(eventHandlerChain, static (ctx, pair) =>
        {
            var ((eventHandlers, compilationInfo), detectedFeatures) = pair;
            if (!detectedFeatures.HasComposition)
                return;
            if (eventHandlers.IsDefaultOrEmpty)
                return;

            if (compilationInfo.Mode != GeneratorMode.Host)
                return;

            var rootNamespace = compilationInfo.RootNamespace;
            var template = new EventHandlerRegistrationTemplate(eventHandlers, rootNamespace);
            var artifact = template.RenderOutput();
            ctx.AddSource(artifact);

            // Emit the AOT-safe typed dispatch table for host-declared handlers too.
            Generators.EventDispatchTableEmitter.Emit(ctx, eventHandlers, rootNamespace);
        });

        // ================================================================
        // Library/Skip mode pipeline (independent — no refMetadata/domainModules/configKeys)
        // Changes to host-level metadata don't trigger Library mode regeneration.
        // ================================================================

        // The raw Compilation is required downstream: DependencyValidator resolves constructor
        // dependencies through the semantic model and LibraryModeGenerator resolves referenced symbols,
        // so this stage re-runs on every edit by design.
        // What the referenced assemblies register, two fields wide: the validator's answer to "who
        // registers this?" for a contract of another module. Narrow on purpose — combining
        // refMetadataProvider itself would re-run this stage whenever any metadata of any category moved.
        var registeredElsewhereProvider = refMetadataProvider
            .Select(static (assemblies, _) => (EquatableArray<RegisteredElsewhere>)
                MetadataReader.ExtractRegisteredContracts(assemblies));

        var libraryProvider = context.CompilationProvider
            .Combine(allStartupSteps)
            .Combine(allServicesProvider)
            .Combine(allDecorators)
            .Combine(allModules)
            .Combine(allEventHandlers)
            .Combine(features)
            .Combine(registeredElsewhereProvider)
            .Combine(validationOnly);

        context.RegisterSourceOutputSafe(libraryProvider, GenerateLibrary);

        // ================================================================
        // Host mode pipeline (needs everything — refMetadata, domainModules, configKeys)
        // ================================================================

        var appSettingsProvider = context.AdditionalTextsProvider
            .Where(static f => System.IO.Path.GetFileName(f.Path)
                .StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
                f.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => file.GetText(ct))
            .Collect()
            .Select(static (texts, _) =>
            {
                var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var text in texts)
                {
                    foreach (var kvp in Validation.ConfigurationValidator.ParseConfigKeys(text))
                        keys[kvp.Key] = kvp.Value;
                }

                return (IReadOnlyDictionary<string, string>)keys;
            });

        // Registrations the features generate into THIS compilation for types the host declares itself.
        // They cannot arrive through refMetadataProvider — see HostLocalRegistration.
        var localRegistrationsProvider = localRegistrations
                                         ?? context.CompilationProvider.Select(
                                             static (_, _) => EquatableArray<MetadataEntry>.Empty);

        // Same idea, different channel: [Boundary] travels as [PragmaticModuleMetadata], which
        // ReadDomainModulesFromReferences reads off references only. The host's own boundaries are
        // handed over by ActionsFeature, which is what writes that attribute.
        var localDomainModulesProvider = localDomainModules
                                         ?? context.CompilationProvider.Select(
                                             static (_, _) => EquatableArray<DiscoveredModuleInfo>.Empty);

        // Same as libraryProvider: HostModeGenerator, DependencyValidator and EventGraphValidator all
        // consume the semantic model / referenced symbols, so the raw Compilation is genuinely needed.
        var hostProvider = context.CompilationProvider
            .Combine(allStartupSteps)
            .Combine(allServicesProvider)
            .Combine(allDecorators)
            .Combine(allModules)
            .Combine(refMetadataProvider)
            .Combine(domainModulesProvider)
            .Combine(features)
            .Combine(appSettingsProvider)
            .Combine(localRegistrationsProvider)
            .Combine(localDomainModulesProvider)
            .Combine(persistedStores);

        context.RegisterSourceOutputSafe(hostProvider, GenerateHost);
    }

    /// <summary>
    ///     Library/Skip mode pipeline — generates metadata attributes for module assemblies.
    ///     Independent from host-level data (refMetadata, domainModules, configKeys).
    /// </summary>
    private static void GenerateLibrary(
        SourceProductionContext context,
        ((((((((Compilation Compilation, ImmutableArray<StartupModel> Startups), ImmutableArray<ServiceModel> Services),
                            ImmutableArray<DecoratorModel> Decorators), ImmutableArray<ModuleModel> Modules),
                        ImmutableArray<EventHandlerModel> EventHandlers),
                    DetectedFeatures Features),
                EquatableArray<RegisteredElsewhere> RegisteredElsewhere),
            EquatableArray<ServiceModel> ToValidate) input)
    {
        var (((((((compilationAndStartups, services), decorators), modules), eventHandlers),
            detectedFeatures), registeredElsewhere), toValidate) = input;
        var (compilation, startups) = compilationAndStartups;

        if (!detectedFeatures.HasComposition)
            return;

        var mode = CompositionDetector.DetermineMode(compilation);
        if (mode == GeneratorMode.Host)
            return; // Host mode handled by GenerateHost

        // Report diagnostics for invalid services and for mis-declared startup steps / decorators /
        // event handlers (dropped as null in the transforms, they would be silently unregistered).
        ReportServiceDiagnostics(context, compilation, services);
        ReportRegistrationDiagnostics(context, compilation, startups, decorators, eventHandlers);

        var validServices = services.Where(s => s.IsValid).ToImmutableArray();
        var validStartups = startups.Where(s => s.IsValid).ToImmutableArray();
        var validDecorators = decorators.Where(d => d.IsValid).ToImmutableArray();
        var validEventHandlers = eventHandlers.Where(e => e.IsValid).ToImmutableArray();

        // The jobs are validated beside the services and registered by nobody here: their own feature
        // writes TryAddScoped<TJob>(), and what was missing was anyone checking what they take.
        var toCheck = toValidate.IsDefaultOrEmpty
            ? services
            : services.AddRange(toValidate.AsImmutableArray());

        if (!toCheck.IsDefaultOrEmpty || !validDecorators.IsDefaultOrEmpty)
            DependencyValidator.Validate(context, compilation, toCheck, validDecorators,
                registeredElsewhere.AsImmutableArray());

        if (mode == GeneratorMode.Library ||
            (mode == GeneratorMode.Skip && (validStartups.Length > 0 || !validServices.IsDefaultOrEmpty ||
                                            !validDecorators.IsDefaultOrEmpty || !modules.IsDefaultOrEmpty ||
                                            !validEventHandlers.IsDefaultOrEmpty)))
        {
            LibraryModeGenerator.Generate(context, compilation, validStartups, validServices, validDecorators, modules,
                validEventHandlers);
        }
    }

    /// <summary>
    ///     Host mode pipeline — generates PragmaticHost entry point, service wiring, endpoints.
    ///     Needs full context: refMetadata, domainModules, configKeys.
    /// </summary>
    private static void GenerateHost(
        SourceProductionContext context,
        (((((((((((Compilation Compilation, ImmutableArray<StartupModel> Startups), ImmutableArray<ServiceModel> Services),
                            ImmutableArray<DecoratorModel> Decorators), ImmutableArray<ModuleModel> Modules),
                        ImmutableArray<AssemblyMetadataModel> RefMetadata),
                    ImmutableArray<DiscoveredModuleInfo> DomainModules),
                DetectedFeatures Features), IReadOnlyDictionary<string, string> ConfigKeys),
            EquatableArray<MetadataEntry> LocalRegistrations),
            EquatableArray<DiscoveredModuleInfo> LocalDomainModules),
            Persistence.Models.PersistedStoresModel PersistedStores) input)
    {
        var ((((((((((compilationAndStartups, services), decorators), modules), refMetadata),
                    domainModules),
                detectedFeatures), configKeys), localRegistrations), localDomainModules), persistedStores) = input;
        var (compilation, startups) = compilationAndStartups;

        if (!detectedFeatures.HasComposition)
            return;

        var mode = CompositionDetector.DetermineMode(compilation);
        if (mode != GeneratorMode.Host)
            return;

        // Report diagnostics for invalid services and for mis-declared startup steps / decorators /
        // event handlers (dropped as null in the transforms, they would be silently unregistered).
        ReportServiceDiagnostics(context, compilation, services);
        // Host mode processes no raw EventHandlerModels (they are library-mode); report startup +
        // decorator mis-declarations for the host project's own registrations.
        ReportRegistrationDiagnostics(context, compilation, startups, decorators, ImmutableArray<EventHandlerModel>.Empty);

        var validServices = services.Where(s => s.IsValid).ToImmutableArray();
        var validStartups = startups.Where(s => s.IsValid).ToImmutableArray();
        var validDecorators = decorators.Where(d => d.IsValid).ToImmutableArray();

        if (!services.IsDefaultOrEmpty || !validDecorators.IsDefaultOrEmpty)
            DependencyValidator.Validate(context, compilation, services, validDecorators,
                MetadataReader.ExtractRegisteredContracts(refMetadata));

        HostModeGenerator.Generate(context, compilation, validStartups, validServices, validDecorators, modules,
            persistedStores, detectedFeatures, refMetadata, domainModules, localRegistrations.AsImmutableArray(),
            localDomainModules.AsImmutableArray());

        // Either half of the channel counts: a module of this application declared it (refMetadata), or
        // the host declared it in its own source (localRegistrations, which its own generator run sees
        // directly and no assembly attribute of its own would carry back to it).
        var takesTheClock =
            refMetadata.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.ClockBindings))
            || localRegistrations.AsImmutableArray().Any(e => e.Category == MetadataCategoryIds.ClockBindings);

        Validation.PragmaticBuilderValidator.Validate(context, detectedFeatures, modules, takesTheClock);

        // Cross-boundary event-graph completeness: dangling events (raised, never handled) → PRAG0816.
        Validation.EventGraphValidator.Validate(context, compilation);

        if (configKeys.Count > 0)
        {
            var remoteBoundaryModules = modules
                .SelectMany(m => m.RemoteBoundaries)
                .Select(rb => rb.ModuleName)
                .Distinct()
                .ToList();

            if (remoteBoundaryModules.Count > 0)
                Validation.ConfigurationValidator.ValidateRemoteBoundaryConfig(
                    context, remoteBoundaryModules, configKeys);
        }
    }

    private static void ReportServiceDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ServiceModel> services)
    {
        // Detect .NET 8+ by checking for IKeyedServiceProvider (added in .NET 8)
        var supportsKeyedServices = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider") is not null;

        foreach (var service in services)
        {
            // Report invalid service reasons
            switch (service.InvalidReason)
            {
                case InvalidReason.Abstract:
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.AbstractClassCannotBeService,
                        service.LocationInfo?.ToLocation(compilation),
                        service.TypeName));
                    break;

                case InvalidReason.NotClass:
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.ServiceRequiresClass,
                        service.LocationInfo?.ToLocation(compilation),
                        service.TypeName));
                    break;

                case InvalidReason.NoInterface:
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.NoInterfaceFound,
                        service.LocationInfo?.ToLocation(compilation),
                        service.TypeName));
                    break;
            }

            if (service.UnresolvedTypeArgument is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.ServiceTypeArgumentNotYetGenerated,
                    service.LocationInfo?.ToLocation(compilation),
                    service.TypeName,
                    service.UnresolvedTypeArgument));

            // Warn about keyed services only on .NET < 8 (where IKeyedServiceProvider doesn't exist)
            if (service.Key is not null && service.InvalidReason == InvalidReason.None && !supportsKeyedServices)
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.KeyedServicesRequireNet8,
                    service.LocationInfo?.ToLocation(compilation),
                    service.TypeName,
                    service.Key));

            // [Inject] members on an open generic cannot be honoured (the open-generic registration
            // path can't run a closed-type factory). Tell the developer instead of dropping them silently.
            if (service is { IsOpenGeneric: true, RequiresFactory: true } && service.InvalidReason == InvalidReason.None)
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.InjectOnOpenGenericUnsupported,
                    service.LocationInfo?.ToLocation(compilation),
                    service.TypeName));
        }
    }

    /// <summary>
    ///     Reports diagnostics for mis-declared [StartupStep] / [Decorator] / [EventHandler] classes.
    ///     The transforms now return an invalid model (instead of null) for these, so the mistake is
    ///     surfaced as an actionable error instead of a silently unregistered — and never run — type.
    /// </summary>
    private static void ReportRegistrationDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<StartupModel> startups,
        ImmutableArray<DecoratorModel> decorators,
        ImmutableArray<EventHandlerModel> eventHandlers)
    {
        foreach (var startup in startups)
        {
            switch (startup.InvalidReason)
            {
                case InvalidReason.NotClass:
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.StartupMustBeClass,
                        startup.LocationInfo?.ToLocation(compilation), startup.TypeName));
                    break;
                case InvalidReason.NotStartupStep:
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.StartupMustImplementInterface,
                        startup.LocationInfo?.ToLocation(compilation), startup.TypeName));
                    break;
            }
        }

        foreach (var decorator in decorators)
            if (decorator.InvalidReason == InvalidReason.NoInterface)
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.DecoratorMustImplementInterface,
                    decorator.LocationInfo?.ToLocation(compilation), decorator.TypeName));

        foreach (var handler in eventHandlers)
            if (handler.InvalidReason == InvalidReason.NotEventHandler)
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.EventHandlerMissingInterface,
                    handler.LocationInfo?.ToLocation(compilation), handler.TypeName));

        ReportHandlersTheOutboxSilences(context, compilation, eventHandlers);
    }

    /// <summary>
    ///     PRAG0837: the <c>[EventHandler]</c>s of a boundary whose events leave through the outbox.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         They are registered and never entered — <c>OutboxInterceptor</c> takes the entity's
    ///         events during the save, and the unit of work takes them after the commit — so the
    ///         handler, the registration and the test that asserts the registration are all green while
    ///         nothing runs.
    ///     </para>
    ///     <para>
    ///         Reported here, in the module's own compilation, because that is where the author wrote
    ///         the handler: a host-level check would name a file in another assembly, or none at all for
    ///         a module consumed as a package. The boundary read is gated on there being a handler to
    ///         report on, so a module without one pays nothing.
    ///     </para>
    /// </remarks>
    private static void ReportHandlersTheOutboxSilences(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<EventHandlerModel> eventHandlers)
    {
        var handlers = eventHandlers.Where(static h => h.IsValid).ToImmutableArray();
        if (handlers.IsDefaultOrEmpty)
            return;

        var boundary = OutboxBoundaryReader
            .TheBoundaryThatSendsItsEventsToTheOutbox(compilation, context.CancellationToken);

        if (boundary is null)
            return;

        foreach (var handler in handlers)
            context.ReportDiagnostic(Diagnostic.Create(
                MessagingDiagnostics.EventHandlerOnOutboxBoundary,
                handler.LocationInfo?.ToLocation(compilation), handler.TypeName, boundary));
    }
}
