using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     Actions feature: registers all pipeline stages for DomainAction, Mutation, Boundary, and Metadata generation.
///     Activated when Pragmatic.Actions runtime is referenced.
/// </summary>
internal static partial class ActionsFeature
{
    /// <summary>
    ///     Returns the Actions metadata this compilation generates for itself, so a host that declares
    ///     its own actions gets the same wiring as one that references a library declaring them, plus the
    ///     <c>[Boundary]</c> declarations it makes — the second, separate channel with the same problem:
    ///     <c>[PragmaticModuleMetadata]</c> is written by this run, so the host cannot read it back.
    /// </summary>
    public static (IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Registrations,
        IncrementalValueProvider<EquatableArray<DerivedPermissionEntry>> DerivedPermissions,
        IncrementalValueProvider<EquatableArray<Composition.Models.DiscoveredModuleInfo>> LocalDomainModules,
        IncrementalValueProvider<PermissionDerivationInputs> Derivation) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<ActionModel>>? traitActions = null,
        IncrementalValueProvider<EquatableArray<PermissionConstEntry>>? permissionCatalog = null,
        IncrementalValueProvider<ImmutableArray<MutationModel>>? programmaticMutations = null,
        // The declared queries, so a read is a member of its boundary like every other operation. They
        // arrive as models rather than symbols for the same reason the scaffolded mutations do: some of
        // them are types this generator writes.
        IncrementalValueProvider<ImmutableArray<Persistence.Models.QueryModel>>? queries = null,
        // The [PragmaticUser] entities, which [FromCurrentUser] on an action or a mutation reads as it does
        // on a query: another declaration of the compilation, so it arrives here rather than being searched for.
        IncrementalValueProvider<EquatableArray<Identity.Models.UserEntityModel>>? currentUsers = null)
    {
        var users = currentUsers ?? context.CompilationProvider.Select(
            static (_, _) => EquatableArray<Identity.Models.UserEntityModel>.Empty);

        // The permission catalogue, for the loads that ask the entity's read permission: its value is
        // spelled by the persistence generator, and reaches this one through the pipeline.
        var loadCatalog = permissionCatalog ?? context.CompilationProvider.Select(
            static (_, _) => EquatableArray<PermissionConstEntry>.Empty);

        // The declared queries, for the [LoadFrom] properties: what a query answers is its model's.
        var declaredQueries = queries ?? context.CompilationProvider.Select(
            static (_, _) => ImmutableArray<Persistence.Models.QueryModel>.Empty);

        // =====================================================================
        // Pipeline 1: [DomainAction] — user-written actions
        // =====================================================================

        var actionProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.DomainAction,
                GeneratorHelpers.IsClass,
                ActionTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(users)
            .Select(static (pair, _) => pair.Left.Bindings is { CurrentUser.Count: 0, CurrentUserLoad: null }
                ? pair.Left
                : pair.Left with { Bindings = InvokerBindingsTransform.Resolve(pair.Left.Bindings, pair.Right) })
            .Combine(loadCatalog)
            .Select(static (pair, _) => pair.Left with
            {
                LoadEntities = LoadReadPermissions.Resolve(pair.Left.LoadEntities, pair.Right)
            })
            .Combine(declaredQueries)
            .Select(static (pair, _) => pair.Left with
            {
                LoadFromQueries = LoadFromQueries.Resolve(pair.Left.LoadFromQueries, pair.Right)
            })
            .WithTrackingName(TrackingNames.ActionsActions);

        // Report diagnostics for invalid actions
        context.RegisterSourceOutputSafe(actionProvider, ReportActionDiagnostics);
        context.RegisterSourceOutputSafe(actionProvider, static (ctx, action) => ReportLoadReadPermissions(
            ctx, action.LoadEntities, action.Location, action.TypeName));
        context.RegisterSourceOutputSafe(actionProvider, static (ctx, action) => ReportLoadFromQueries(
            ctx, action.LoadFromQueries, action.Location, action.TypeName));
        context.RegisterSourceOutputSafe(actionProvider, static (ctx, action) => ReportBindings(
            ctx, action.TypeName, action.Bindings, action.LoadedValidation));

        // Generate per-action files for valid models only
        var validActions = actionProvider.Where(static m => m.IsValid);

        context.RegisterSourceOutputSafe(validActions, GenerateSetDependencies);
        context.RegisterSourceOutputSafe(validActions, GenerateLoadEntity);
        context.RegisterSourceOutputSafe(validActions, GenerateInvoker);
        context.RegisterSourceOutputSafe(validActions, GenerateVersioning);

        // Which operations have a [Validator] in this compilation: the async opt-in, decided here for
        // actions and mutations alike instead of looked up at runtime (see ActionsFeature.AsyncValidators).
        var asyncValidatedTypes = AsyncValidatedTypes(context);
        context.RegisterSourceOutputSafe(
            validActions.Combine(asyncValidatedTypes),
            static (ctx, pair) => GenerateValidationMetadata(ctx, pair.Left, pair.Right));

        // =====================================================================
        // Aggregate: collect all valid actions for registration + metadata
        // =====================================================================

        var allActions = validActions.Collect()
            .WithTrackingName(TrackingNames.ActionsAllActions);

        // Merge trait-generated actions into the pipeline (Phase 3)
        if (traitActions is not null)
        {
            allActions = allActions.Combine(traitActions.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

            // Per-action generation for trait actions (Invoker + SetDependencies)
            var traitActionItems = traitActions.Value
                .SelectMany(static (models, _) => models);
            context.RegisterSourceOutputSafe(traitActionItems, GenerateSetDependencies);
            context.RegisterSourceOutputSafe(traitActionItems, GenerateInvoker);
        }

        // =====================================================================
        // Pipeline 2: [Mutation] — entity mutation invokers
        // =====================================================================

        var mutationProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Mutation,
                GeneratorHelpers.IsClassOrRecord,
                MutationTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(users)
            .Select(static (pair, _) => pair.Left.Bindings is { CurrentUser.Count: 0, CurrentUserLoad: null }
                ? pair.Left
                : pair.Left with { Bindings = InvokerBindingsTransform.Resolve(pair.Left.Bindings, pair.Right) })
            .Combine(loadCatalog)
            .Select(static (pair, _) => pair.Left with
            {
                LoadEntities = LoadReadPermissions.Resolve(pair.Left.LoadEntities, pair.Right)
            })
            .Combine(declaredQueries)
            .Select(static (pair, _) => pair.Left with
            {
                LoadFromQueries = LoadFromQueries.Resolve(pair.Left.LoadFromQueries, pair.Right)
            })
            .WithTrackingName(TrackingNames.ActionsMutations);

        context.RegisterSourceOutputSafe(mutationProvider, static (ctx, mutation) => ReportLoadReadPermissions(
            ctx, mutation.LoadEntities, mutation.Location, mutation.TypeName));
        context.RegisterSourceOutputSafe(mutationProvider, static (ctx, mutation) => ReportLoadFromQueries(
            ctx, mutation.LoadFromQueries, mutation.Location, mutation.TypeName));

        context.RegisterSourceOutputSafe(mutationProvider, static (ctx, mutation) => ReportBindings(
            ctx, mutation.TypeName, mutation.Bindings, mutation.LoadedValidation));

        // Report diagnostics for invalid mutations
        // The diagnostics go through the detector as the generation does: PRAG0445 exists only where
        // Mapping is NOT referenced, and «is it?» has one answer across the whole generator.
        context.RegisterSourceOutputSafe(
            mutationProvider.Combine(features),
            static (ctx, pair) => ReportMutationDiagnostics(
                ctx, pair.Left with { MappingOwnsTheBody = pair.Right.HasMapping, ValidationIsAvailable = pair.Right.HasValidation }));

        // Generate per-mutation files for valid models only
        var validMutations = mutationProvider.Where(static m => m.IsValid);

        context.RegisterSourceOutputSafe(validMutations, GenerateMutationSetDependencies);
        context.RegisterSourceOutputSafe(validMutations, GenerateMutationLoadEntity);
        context.RegisterSourceOutputSafe(validMutations, GenerateMutationId);
        context.RegisterSourceOutputSafe(validMutations, GenerateMutationLogicalKey);
        // The body of the write: Mapping's where Mapping is referenced, this template's where it is not.
        // The detector asks, and both generators read the same answer instead of coordinating.
        context.RegisterSourceOutputSafe(
            validMutations.Combine(features),
            static (ctx, pair) => GenerateMutationAutoMap(
                ctx, pair.Left with { MappingOwnsTheBody = pair.Right.HasMapping, ValidationIsAvailable = pair.Right.HasValidation }));
        // The invoker overrides ValidateNestedTree only where Validation is referenced.
        context.RegisterSourceOutputSafe(
            validMutations.Combine(features),
            static (ctx, pair) => GenerateMutationInvoker(
                ctx, pair.Left with { ValidationIsAvailable = pair.Right.HasValidation }));
        // The metadata names ValidationError, so it exists only where Validation is referenced; where it
        // is not, the runtime falls back to asking the container, as it did for every mutation before.
        context.RegisterSourceOutputSafe(
            validMutations.Combine(asyncValidatedTypes).Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasValidation)
                    return;
                GenerateMutationValidationMetadata(ctx, pair.Left.Left, pair.Left.Right);
            });

        var allMutations = validMutations.Collect()
            .WithTrackingName(TrackingNames.ActionsAllMutations);

        // Mutations another feature builds as models rather than reading from syntax — [Resource] CRUD.
        // The same shape as traitActions above, and for the same reason: they must reach every stage a
        // hand-written mutation reaches, or the scaffolded write is a second pipeline that quietly
        // skips validation, permissions, the commit strategy and the event hand-over.
        if (programmaticMutations is not null)
        {
            allMutations = allMutations.Combine(programmaticMutations.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

            var programmaticMutationItems = programmaticMutations.Value
                .SelectMany(static (models, _) => models);
            context.RegisterSourceOutputSafe(programmaticMutationItems, GenerateMutationSetDependencies);
            context.RegisterSourceOutputSafe(programmaticMutationItems, GenerateMutationId);
            context.RegisterSourceOutputSafe(programmaticMutationItems, GenerateMutationLogicalKey);
            context.RegisterSourceOutputSafe(programmaticMutationItems, GenerateMutationAutoMap);
            context.RegisterSourceOutputSafe(programmaticMutationItems, GenerateMutationInvoker);
        }

        // =====================================================================
        // Pipeline 4: [CompositeAction] — composite invoker for mutation-step pattern
        // =====================================================================

        var compositeProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.CompositeAction,
                GeneratorHelpers.IsClass,
                CompositeActionTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutputSafe(compositeProvider, ReportCompositeDiagnostics);

        // =====================================================================
        // Pipeline 3: [Boundary] — interface generation (Actions + Mutations)
        // =====================================================================

        var boundaryProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Boundary,
                GeneratorHelpers.IsClass,
                BoundaryTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.ActionsBoundaries);

        // Report diagnostics for invalid boundaries
        context.RegisterSourceOutputSafe(boundaryProvider, ReportBoundaryDiagnostics);

        // A module that declares no [Boundary] gets one, derived from its own [Module]. It has to be
        // SYNTHESISED rather than emitted-then-discovered: this generator does not see its own output,
        // so a class it writes carrying [Boundary] would never reach `boundaryProvider` and everything
        // downstream — interface, DbContext, module metadata — would have nothing to hang off.
        // ⚠️ The union goes through Collect(), so the boundary stage loses per-symbol incrementality.
        // There is one boundary per assembly and the stage below already re-runs on every edit by
        // design, so the cost is a comparison of a single-element array.
        var declaredModules = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Composition.Attributes.ModuleAttribute",
                GeneratorHelpers.IsClass,
                static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static s => s is not null)
            .Select(static (s, _) => DefaultBoundaryTransform.FromModule(s!))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        var isLibrary = context.CompilationProvider
            .Select(static (c, _) => !CompositionDetector.IsHostProject(c));

        var validBoundaries = boundaryProvider.Where(static m => m.IsValid).Collect()
            .Combine(declaredModules)
            .Combine(isLibrary)
            .SelectMany(static (input, _) =>
            {
                var ((declared, fromModules), library) = input;
                if (!declared.IsEmpty || !library || fromModules.Length != 1)
                    return declared;

                return fromModules;
            })
            .WithTrackingName(TrackingNames.ActionsEffectiveBoundaries);

        // The class itself, once — a declared boundary already exists in source.
        context.RegisterSourceOutputSafe(
            validBoundaries.Where(static m => m.IsGenerated),
            static (spc, boundary) =>
            {
                var artifact = new DefaultBoundaryTemplate(boundary).RenderOutput();
                if (!artifact.IsEmpty)
                    spc.AddSource(artifact);
            });

        // Every boundary, declared or generated, is an IBoundary: the constraint an application's
        // BoundaryConfiguration<TBoundary> — and so its UseDatabase — needs.
        context.RegisterSourceOutputSafe(
            validBoundaries,
            static (spc, boundary) =>
            {
                var artifact = new BoundaryMarkerTemplate(boundary).RenderOutput();
                if (!artifact.IsEmpty)
                    spc.AddSource(artifact);
            });

        // ⚠️ The composite needs the boundaries, which is why it is generated here and not next to its
        // other outputs. A composite has no entity to derive its boundary from — a mutation does — so
        // the boundary comes from the namespace, through the same matcher that serves every other member.
        // Without one, the composite would ask for an unkeyed IUnitOfWork nobody registers, and the
        // application would fail to start with a container error naming neither the composite nor the
        // missing attribute; where the namespace is not enough, PRAG0434 says so at build time.
        context.RegisterSourceOutputSafe(
            compositeProvider.Where(static m => !m.Steps.IsDefaultOrEmpty)
                .Combine(validBoundaries.Collect()),
            static (spc, pair) => GenerateCompositeInvoker(spc, pair.Left, pair.Right));

        // Combine boundaries with all actions AND all mutations AND compilation (for package metadata).
        // The raw Compilation is required here: GenerateBoundaryInterfaces reads [UsePackage<T>] action
        // registrations out of REFERENCED assemblies' metadata and probes System.Net.Http.HttpClient —
        // neither is expressible as a scalar projection, so this stage re-runs on every edit by design.
        var queryMembers = queries ?? context.CompilationProvider.Select(
            static (_, _) => ImmutableArray<Persistence.Models.QueryModel>.Empty);

        var boundaryWithMembers = validBoundaries.Collect()
            .Combine(allActions)
            .Combine(allMutations)
            .Combine(queryMembers)
            .Combine(context.CompilationProvider);
        context.RegisterSourceOutputSafe(boundaryWithMembers, GenerateBoundaryInterfaces);

        // Compilation info for metadata generation (Composition detection + Debug mode)
        var compilationInfo = context.CompilationProvider
            .Select(static (c, _) => (
                IsDebug: c.Options.OptimizationLevel == OptimizationLevel.Debug,
                HasComposition: CompositionDetector.IsCompositionReferenced(c)));

        // Resolve [RequirePermission] constant references against the permission catalog before the
        // registry/metadata stages consume permissions — a source generator cannot resolve constants it
        // generates itself, so unresolved references were captured from syntax and are resolved here.
        var resolvedActions = permissionCatalog is null
            ? allActions
            : allActions.Combine(permissionCatalog.Value).Select(static (p, _) => ResolveActionPermissions(p.Left, p.Right));
        var resolvedMutations = permissionCatalog is null
            ? allMutations
            : allMutations.Combine(permissionCatalog.Value).Select(static (p, _) => ResolveMutationPermissions(p.Left, p.Right));

        // A permission attribute with no permission never reaches the registry, so the
        // pipeline reads it as "no requirement". Reject it while the declaration is still visible.
        // Unconditional — unlike the constant resolution below, this needs no catalog.
        context.RegisterSourceOutputSafe(
            allActions.Combine(allMutations), ReportEmptyPermissionRequirements);

        // Surface any [RequirePermission] constant the catalog could not resolve — it would fail open.
        if (permissionCatalog is not null)
        {
            context.RegisterSourceOutputSafe(allActions.Combine(permissionCatalog.Value), ReportUnresolvedActionPermissions);
            context.RegisterSourceOutputSafe(allMutations.Combine(permissionCatalog.Value), ReportUnresolvedMutationPermissions);
        }

        // Opt-in auto-derivation. It sits after the constant resolution above and before every stage that
        // reads permissions, so the derived name travels the same path a hand-written one does. With the
        // switch off each selector returns its input array unchanged — the stages below cannot tell this
        // stage is here, which is the whole contract of the switch.
        var autoDeriveEnabled = AutoDeriveEnabled(context);
        var catalogOrEmpty = permissionCatalog ?? context.CompilationProvider
            .Select(static (_, _) => EquatableArray<PermissionConstEntry>.Empty);
        // One value for every feature that derives: this one for actions and mutations, Endpoints for
        // a [Query] — which has no model here and is protected by its route alone. ⚠️ Until Endpoints
        // received it, a query under the posture required nothing.
        var derivationInputs = validBoundaries.Collect()
            .Combine(catalogOrEmpty)
            .Combine(autoDeriveEnabled)
            .Select(static (p, _) => new PermissionDerivationInputs(p.Left.Left, p.Left.Right, p.Right));

        resolvedActions = resolvedActions.Combine(derivationInputs).Select(static (p, _) =>
            DeriveActionPermissions(p.Left, p.Right.Boundaries.AsImmutableArray(), p.Right.Catalog, p.Right.Enabled));
        resolvedMutations = resolvedMutations.Combine(derivationInputs).Select(static (p, _) =>
            DeriveMutationPermissions(p.Left, p.Right.Boundaries.AsImmutableArray(), p.Right.Catalog, p.Right.Enabled));

        // An [ExplicitPermission(Constant)] the catalog cannot answer for falls back to the derived name — safe,
        // but not what was written. PRAG0421 makes that visible instead of silent.
        context.RegisterSourceOutputSafe(
            allActions.Combine(allMutations).Combine(catalogOrEmpty.Combine(autoDeriveEnabled)),
            ReportUnresolvedExplicitPermissions);

        // Seeding — a derived permission that appears in no catalog can only deny — happens in
        // RegisterDerivedPermissionCatalog, called from the entry point once the reads have been derived
        // too. The names this stage produced travel there through DerivedPermissions below.
        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "");

        // Actions + Mutations registration (aggregate DI extension methods).
        // Both are fed the combined actions+mutations view so the standalone registration
        // can also register the generated authorization registries (otherwise the empty defaults in
        // AddPragmaticActions win and [RequirePermission]/[RequirePolicy] fail open).
        var actionsAndMutationsForRegistry = resolvedActions.Combine(resolvedMutations);
        context.RegisterSourceOutputSafe(actionsAndMutationsForRegistry, GenerateActionsRegistration);
        context.RegisterSourceOutputSafe(actionsAndMutationsForRegistry, GenerateMutationsRegistration);
        context.RegisterSourceOutputSafe(actionsAndMutationsForRegistry, GeneratePolicyRegistry);

        // Permission requirement registry (eliminates reflection in PermissionAuthorizationFilter)
        // Combines actions + mutations since both can have [RequirePermission]
        context.RegisterSourceOutputSafe(actionsAndMutationsForRegistry, GeneratePermissionRequirementRegistry);

        // Actions + Mutations metadata (enriched with invoker types for host-side DI)
        var actionsWithMutationsAndCompilation = resolvedActions.Combine(resolvedMutations).Combine(compilationInfo);
        context.RegisterSourceOutputSafe(actionsWithMutationsAndCompilation, GenerateActionsMetadata);

        // Boundary module metadata (cross-assembly discovery)
        var boundaryWithCompilation = validBoundaries.Combine(compilationInfo);
        context.RegisterSourceOutputSafe(boundaryWithCompilation, GenerateBoundaryModuleMetadata);

        // The Actions payload, plus — when auto-derivation produced anything — the entry point for the
        // catalog this compilation generates for itself. A host that declares its own actions cannot
        // learn it from [assembly: PragmaticMetadata]: that attribute is written by this same run.
        var registrations = actionsWithMutationsAndCompilation.Combine(assemblyName)
            .Select(static (input, _) => LocalActionsRegistrations(input.Left, input.Right));

        // Handed to the manifest, which is built from endpoint models and so cannot see a name that is
        // on no attribute — and to the catalog, once Endpoints has added the queries' names.
        var derivedPermissions = resolvedActions.Combine(resolvedMutations)
            .Select(static (pair, _) => CollectDerivedPermissions(pair.Left, pair.Right));

        // The same boundaries GenerateBoundaryModuleMetadata describes in [PragmaticModuleMetadata],
        // handed to the host directly. Built through the reader's own factory, so the module name the
        // host derives is the one it would have derived from the attribute.
        var localDomainModules = validBoundaries.Collect().Combine(assemblyName.Combine(compilationInfo))
            .Select(static (input, _) => LocalDomainModulesOf(input.Left, input.Right.Left, input.Right.Right));

        return (registrations, derivedPermissions, localDomainModules, derivationInputs);
    }

    /// <summary>
    ///     The <c>[Boundary]</c> declarations of this compilation, as the host reads them off a reference.
    ///     Empty unless Composition is in play — the same gate
    ///     <see cref="GenerateBoundaryModuleMetadata"/> applies to the attribute itself, so the two never
    ///     disagree about whether a boundary is discoverable.
    /// </summary>
    private static EquatableArray<Composition.Models.DiscoveredModuleInfo> LocalDomainModulesOf(
        ImmutableArray<BoundaryModel> boundaries,
        string assemblyName,
        (bool IsDebug, bool HasComposition) compilationInfo)
    {
        if (!compilationInfo.HasComposition || boundaries.IsDefaultOrEmpty)
            return EquatableArray<Composition.Models.DiscoveredModuleInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<Composition.Models.DiscoveredModuleInfo>();
        foreach (var boundary in boundaries.OrderBy(b => b.FullTypeName, StringComparer.Ordinal))
            // Both are already SymbolDisplayFormat.FullyQualifiedFormat — the same strings the template
            // puts inside typeof(...), and therefore the same ones the reader gets back off a reference.
            builder.Add(Composition.Transforms.MetadataReader.CreateDomainModule(
                assemblyName,
                boundary.FullTypeName,
                readAccessTypes: boundary.ReadAccessTypes.AsImmutableArray()));

        return builder.ToImmutable();
    }

    private static void GeneratePolicyRegistry(SourceProductionContext ctx,
        (ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) pair)
    {
        // Defensive: HasPolicy without a resolved PolicyTypeFullName would be a
        // transform bug — skip the entry instead of emitting null into the
        // generated source. The diagnostic guard below surfaces the problem.
        var entries = pair.Actions
            .Where(a => a.IsValid && a.HasPolicy && !string.IsNullOrEmpty(a.PolicyTypeFullName))
            .Select(a => new PolicyRegistryTemplate.PolicyEntry(a.FullTypeName, a.PolicyTypeFullName!))
            .Concat(pair.Mutations
                .Where(m => m.IsValid && m.HasPolicy && !string.IsNullOrEmpty(m.PolicyTypeFullName))
                .Select(m => new PolicyRegistryTemplate.PolicyEntry(m.FullTypeName, m.PolicyTypeFullName!)))
            .ToImmutableArray();

        foreach (var a in pair.Actions)
        {
            if (a.IsValid && a.HasPolicy && string.IsNullOrEmpty(a.PolicyTypeFullName))
                ctx.ReportDiagnostic(Diagnostic.Create(ActionsDiagnostics.PolicyTypeUnresolved, location: null, a.FullTypeName));
        }
        foreach (var m in pair.Mutations)
        {
            if (m.IsValid && m.HasPolicy && string.IsNullOrEmpty(m.PolicyTypeFullName))
                ctx.ReportDiagnostic(Diagnostic.Create(ActionsDiagnostics.PolicyTypeUnresolved, location: null, m.FullTypeName));

            // [RequirePolicy<T>] on a mutation is enforced at runtime by
            // MutationInvoker.CheckPolicyAndResourceAsync and its factory lands in the generated
            // policy registry (the mutations concat above), so it is a supported, first-class feature.
        }

        // Emit whenever this assembly HAS actions, not only when one of them declares something. An
        // assembly with actions and no declarations must still produce a registry, so that "nothing
        // declared" and "the generator never ran" are different artifacts: no artifact at all means
        // only the second, and the runtime fails closed on it.
        if (pair.Actions.IsDefaultOrEmpty && pair.Mutations.IsDefaultOrEmpty)
            return;

        var ns = pair.Actions.FirstOrDefault(a => !string.IsNullOrEmpty(a.Namespace))?.Namespace
              ?? pair.Mutations.FirstOrDefault(m => !string.IsNullOrEmpty(m.Namespace))?.Namespace
              ?? "";
        var template = new PolicyRegistryTemplate(entries, ns);
        var artifact = template.RenderOutput();
        ctx.AddSource(artifact);
    }

    /// <summary>
    ///     Computes the namespaces of the generated authorization registries so the
    ///     standalone registration can register them. Returns null for a registry that was not
    ///     generated. The namespace must match what GeneratePermissionRequirementRegistry /
    ///     GeneratePolicyRegistry use (first action-or-mutation namespace).
    /// </summary>
    internal static (string? PermissionNs, string? PolicyNs) ComputeAuthorizationRegistryNamespaces(
        ImmutableArray<ActionModel> actions, ImmutableArray<MutationModel> mutations)
    {
        var ns = actions.FirstOrDefault(a => !string.IsNullOrEmpty(a.Namespace))?.Namespace
              ?? mutations.FirstOrDefault(m => !string.IsNullOrEmpty(m.Namespace))?.Namespace
              ?? "";

        // Both registries are emitted whenever the assembly has actions at all — see the note in
        // GeneratePermissionRequirementRegistry. This must stay in step with that guard: a namespace
        // returned here that no registry was emitted for produces a registration for a type that
        // does not exist.
        var hasAny = !actions.IsDefaultOrEmpty || !mutations.IsDefaultOrEmpty;

        return (hasAny ? ns : null, hasAny ? ns : null);
    }

    private static void GeneratePermissionRequirementRegistry(SourceProductionContext ctx,
        (ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) pair)
    {
        var entries = pair.Actions
            .Where(a => a.IsValid && a.HasPermissionRequirement)
            .Select(a =>
            {
                if (!a.RequireAllPermissions.IsDefaultOrEmpty)
                    return new PermissionRequirementRegistryTemplate.PermissionEntry(a.FullTypeName, a.RequireAllPermissions.AsImmutableArray(), RequireAll: true);
                return new PermissionRequirementRegistryTemplate.PermissionEntry(a.FullTypeName, a.RequireAnyPermissions.AsImmutableArray(), RequireAll: false);
            })
            .Concat(pair.Mutations
                .Where(m => m.IsValid && m.HasPermissionRequirement)
                .Select(m =>
                {
                    if (!m.RequireAllPermissions.IsDefaultOrEmpty)
                        return new PermissionRequirementRegistryTemplate.PermissionEntry(m.FullTypeName, m.RequireAllPermissions.AsImmutableArray(), RequireAll: true);
                    return new PermissionRequirementRegistryTemplate.PermissionEntry(m.FullTypeName, m.RequireAnyPermissions.AsImmutableArray(), RequireAll: false);
                }))
            .ToImmutableArray();

        // Emit whenever this assembly HAS actions, not only when one of them declares something. An
        // assembly with actions and no declarations must still produce a registry, so that "nothing
        // declared" and "the generator never ran" are different artifacts: no artifact at all means
        // only the second, and the runtime fails closed on it.
        if (pair.Actions.IsDefaultOrEmpty && pair.Mutations.IsDefaultOrEmpty)
            return;

        var ns = pair.Actions.FirstOrDefault(a => !string.IsNullOrEmpty(a.Namespace))?.Namespace
              ?? pair.Mutations.FirstOrDefault(m => !string.IsNullOrEmpty(m.Namespace))?.Namespace
              ?? "";
        var template = new PermissionRequirementRegistryTemplate(entries, ns);
        var artifact = template.RenderOutput();
        ctx.AddSource(artifact);
    }
}
