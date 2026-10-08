using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Diagnostics;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Templates;
using Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

namespace Pragmatic.SourceGenerator.Features.Endpoints;

/// <summary>
///     Endpoints feature: registers all pipeline stages for endpoint handler, body DTO,
///     autocomplete, registration, metadata, and contract generation.
///     Activated when Pragmatic.Endpoints runtime is referenced.
/// </summary>
internal static partial class EndpointsFeature
{
    /// <summary>
    ///     Returns the collected endpoint models for consumption by ManifestFeature, the Endpoints
    ///     metadata this compilation generates for itself so a host that declares its own endpoints gets
    ///     them mapped, and every permission the auto-derivation posture produced — the ones Actions
    ///     handed in plus the ones this feature gave its queries — for the catalog.
    /// </summary>
    public static (IncrementalValueProvider<ImmutableArray<EndpointModel>> Endpoints,
        IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> LocalRegistrations,
        IncrementalValueProvider<EquatableArray<DerivedPermissionEntry>> DerivedPermissions) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<EndpointModel>>? programmaticEndpoints = null,
        IncrementalValueProvider<ImmutableArray<Manifest.Models.ManifestTypeModel>>? programmaticTypes = null,
        IncrementalValueProvider<EquatableArray<PermissionConstEntry>>? permissionCatalog = null,
        IncrementalValueProvider<EquatableArray<DerivedPermissionEntry>>? derivedPermissions = null,
        IncrementalValueProvider<Actions.Models.PermissionDerivationInputs>? derivation = null)
    {
        // ── Pipeline 1: [Endpoint] manual endpoints ──
        var endpointProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Endpoint,
                IsPartialClass,
                EndpointTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.EndpointsEndpoints);

        // Report diagnostics — always, regardless of feature gate (so the user gets errors)
        context.RegisterSourceOutputSafe(endpointProvider, ReportDiagnostics);

        // ── [Endpoint] on a member: only a promoted specification is read there ──
        // The attribute's targets were widened so a specification could declare where its derived query
        // answers. Every other member carrying it is a route nobody maps, and PRAG0525 says so rather
        // than letting the declaration read as one that works.
        var endpointOnMember = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Endpoint,
                static (node, _) => node is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                static (ctx, _) => DerivedRouteHost.Describe(ctx));

        context.RegisterSourceOutputSafe(endpointOnMember, static (ctx, member) =>
        {
            if (member.DerivesAQuery)
                return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.EndpointOnAMemberNothingDerives,
                member.Location?.ToLocation(),
                member.MemberName));
        });

        // Collect all valid endpoints, gated by HasEndpoints
        var validEndpoints = endpointProvider
            .Combine(features)
            .Where(static pair => pair.Right.HasEndpoints && pair.Left.IsValid)
            .Select(static (pair, _) => pair.Left);

        // A [RequirePermission] written as a generated constant reaches the transform unbound, so it is
        // turned into a permission value here — before anything consumes the model. Skipping this leaves
        // the handler with no authorization at all, which no test of the generated output would notice.
        if (permissionCatalog is not null)
        {
            context.RegisterSourceOutputSafe(
                validEndpoints.Combine(permissionCatalog.Value), ReportUnresolvedEndpointPermissions);

            validEndpoints = validEndpoints.Combine(permissionCatalog.Value)
                .Select(static (pair, _) => ResolveEndpointPermissions(pair.Left, pair.Right));
        }

        // The auto-derivation posture, for the queries: after the constant resolution above, because a
        // hand-written requirement — resolved or still a path — wins over a derived one, and before the
        // handler is generated, because the route is the only place a query is protected.
        if (derivation is not null)
        {
            context.RegisterSourceOutputSafe(
                validEndpoints.Combine(derivation.Value), ReportUnresolvedExplicitQueryPermissions);

            validEndpoints = validEndpoints.Combine(derivation.Value)
                .Select(static (pair, _) => DeriveQueryPermission(pair.Left, pair.Right));
        }

        // The handlers are generated further down, once the reads a Create's Location can point at are
        // known — they come from every endpoint, the programmatic ones included.

        // ── Pipeline 2: [Autocomplete] and [Autocomplete<TDto>] property-level endpoints ──
        var autocompleteDefaultProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EndpointAttributeNames.Autocomplete,
                IsPropertyNode,
                AutocompleteTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        var autocompleteGenericProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EndpointAttributeNames.AutocompleteGeneric,
                IsPropertyNode,
                AutocompleteTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Merge both providers
        var autocompleteProvider = autocompleteDefaultProvider
            .Collect()
            .Combine(autocompleteGenericProvider.Collect())
            .SelectMany(static (pair, _) => pair.Left.AddRange(pair.Right));

        // Report diagnostics for invalid autocomplete models — always
        context.RegisterSourceOutputSafe(autocompleteProvider, ReportAutocompleteDiagnostics);

        // Generate autocomplete endpoint handlers, gated by HasEndpoints
        var validAutocomplete = autocompleteProvider
            .Combine(features)
            .Where(static pair => pair.Right.HasEndpoints && pair.Left.IsValid)
            .Select(static (pair, _) => pair.Left);

        // Just the model: the autocomplete endpoint enforces its read permission in the one shape there
        // is, and whether anybody can hold that permission is the host's question, answered
        // by PRAG1692 where the host is. Neither of those needs HasIdentityAspNetCore here.
        context.RegisterSourceOutputSafe(validAutocomplete, GenerateAutocompleteEndpoint);

        // ── Combined registration: merge all endpoint types ──
        var manualEndpointModels = validEndpoints.Collect()
            .WithTrackingName(TrackingNames.EndpointsManualEndpoints);

        // ── Programmatic endpoints (from traits/resources) ──
        // "Dev endpoint wins": filter out programmatic endpoints that overlap with manual ones.
        // Compute the filtered set ONCE, then reuse it for both handler generation and the
        // registration/metadata merge — emitting (and filtering) each endpoint a single time.
        IncrementalValueProvider<ImmutableArray<EndpointModel>>? filteredProgrammatic = null;
        if (programmaticEndpoints is not null)
        {
            var resolvedProgrammatic = programmaticEndpoints.Value;

            // The same resolution the manual pipeline does above, for the endpoints this generator
            // builds as models. A developer decorating a scaffolded operation writes
            // [RequirePermission(BookingPermissions.Guest.Read)] — a constant this generator emits, so
            // the transform keeps it as a path and something has to turn it into a value.
            //
            // Without it the permission would be dropped and the endpoint mapped with a bare
            // RequireAuthorization(), open to any authenticated caller — and nothing would fail: not the
            // build, not PRAG0528 (which is reported on the manual pipeline only), not a test that runs
            // as a caller holding everything.
            if (permissionCatalog is not null)
            {
                context.RegisterSourceOutputSafe(
                    resolvedProgrammatic.SelectMany(static (endpoints, _) => endpoints)
                        .Combine(permissionCatalog.Value),
                    ReportUnresolvedEndpointPermissions);

                resolvedProgrammatic = resolvedProgrammatic.Combine(permissionCatalog.Value)
                    .Select(static (pair, _) => pair.Left.IsDefaultOrEmpty
                        ? pair.Left
                        : pair.Left.Select(e => ResolveEndpointPermissions(e, pair.Right)).ToImmutableArray());
            }

            filteredProgrammatic = resolvedProgrammatic
                .Combine(manualEndpointModels)
                .Select(static (pair, _) =>
                {
                    var (programmatic, manual) = pair;
                    if (programmatic.IsDefaultOrEmpty) return ImmutableArray<EndpointModel>.Empty;

                    var manualRoutes = new HashSet<string>(
                        manual.Select(e => $"{e.HttpMethod}:{e.Route}"),
                        StringComparer.OrdinalIgnoreCase);

                    return programmatic.Where(p =>
                        !manualRoutes.Contains($"{p.HttpMethod}:{p.Route}")).ToImmutableArray();
                })
                // The response writer of an endpoint assembled here: its model carries type text, not a symbol,
                // so the type is resolved against the compilation, as the request bodies of the same endpoints are.
                .Combine(context.CompilationProvider)
                .Select(static (pair, _) =>
                {
                    var (endpoints, compilation) = pair;
                    if (endpoints.IsDefaultOrEmpty) return endpoints;

                    var resolver = new Serialization.Analysis.JsonTypeExpressionResolver(compilation);
                    return endpoints.Select(e => EndpointResponseWriter.With(e, compilation, resolver)).ToImmutableArray();
                });
        }

        // Every Single read of the compilation, for the Location of a Create that answers at its route.
        var readRoutes = filteredProgrammatic is null
            ? manualEndpointModels.Select(static (manual, _) => CollectReadRoutes(manual))
            : manualEndpointModels.Combine(filteredProgrammatic.Value)
                .Select(static (pair, _) => CollectReadRoutes(pair.Left.AddRange(pair.Right)));

        // Generate individual endpoint handler partials (one source each), manual and programmatic.
        context.RegisterSourceOutputSafe(
            validEndpoints.Combine(readRoutes).Select(static (pair, _) => WithReadLocation(pair.Left, pair.Right)),
            GenerateEndpointHandler);

        if (filteredProgrammatic is not null)
        {
            context.RegisterSourceOutputSafe(
                filteredProgrammatic.Value.SelectMany(static (endpoints, _) => endpoints)
                    .Combine(readRoutes).Select(static (pair, _) => WithReadLocation(pair.Left, pair.Right)),
                GenerateEndpointHandler);
        }

        var autocompleteEndpointModels = validAutocomplete
            .Select(static (m, _) => m.ToEndpointModelForRegistration())
            .Collect();

        var allEndpoints = manualEndpointModels
            .Combine(autocompleteEndpointModels)
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        // Merge the already-filtered programmatic endpoints into the registration set.
        if (filteredProgrammatic is not null)
        {
            allEndpoints = allEndpoints.Combine(filteredProgrammatic.Value)
                .Select(static (pair, _) => pair.Right.IsDefaultOrEmpty
                    ? pair.Left
                    : pair.Left.AddRange(pair.Right));
        }

        allEndpoints = allEndpoints.WithTrackingName(TrackingNames.EndpointsAllEndpoints);

        // Compilation info for metadata generation (Composition detection + Debug mode)
        var compilationInfo = context.CompilationProvider
            .Select(static (c, _) => (
                IsDebug: c.Options.OptimizationLevel == OptimizationLevel.Debug,
                HasComposition: CompositionDetector.IsCompositionReferenced(c)));

        // Generate assembly-level metadata for cross-assembly discovery (enriched JSON)
        var endpointsWithCompilation = allEndpoints.Combine(compilationInfo);
        context.RegisterSourceOutputSafe(endpointsWithCompilation, GenerateEndpointMetadata);

        // Generate assembly-level endpoint contracts (route, method, error types, permissions)
        context.RegisterSourceOutputSafe(allEndpoints, GenerateEndpointContracts);

        // The UTF-8 writers the handlers answer through, one file for the assembly.
        context.RegisterSourceOutputSafe(
            allEndpoints.Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "")),
            static (ctx, pair) => GenerateResponseWriters(ctx, pair.Left, pair.Right));

        // Generate ApiRoutes (compile-time route constants + typed URL builders per boundary)
        var routesInput = allEndpoints.Combine(
            context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Api"));
        context.RegisterSourceOutputSafe(routesInput, GenerateApiRoutes);

        // Generate API manifest (aggregated JSON for OpenAPI, client generation, tooling).
        // The raw Compilation is required: the manifest resolves request/response DTO and entity SYMBOLS
        // (GetTypeByMetadataName + member walking) to emit their property shapes, so there is no scalar
        // projection that preserves the information. This stage re-runs on every edit by design.
        // Permissions Actions derived are contributed the same way, and for the same reason: they are on
        // no attribute, so nothing here could read them off the endpoint's symbol. The queries' derived
        // names are on the endpoint models themselves, and are listed with the same source label.
        var allDerivedPermissions = (derivedPermissions ?? context.CompilationProvider
                .Select(static (_, _) => EquatableArray<DerivedPermissionEntry>.Empty))
            .Combine(manualEndpointModels.Select(static (endpoints, _) => CollectDerivedQueryPermissions(endpoints)))
            .Select(static (pair, _) => pair.Right.IsDefaultOrEmpty
                ? pair.Left
                : new EquatableArray<DerivedPermissionEntry>(
                    pair.Left.AsImmutableArray().AddRange(pair.Right.AsImmutableArray())));

        var manifestInput = allEndpoints.Combine(context.CompilationProvider)
            .Combine(allDerivedPermissions);
        // Types generated by this or other features cannot be resolved from the compilation, so they
        // arrive as descriptions and are merged in: the records a key-returning mutation answers with,
        // and whatever the other features contribute.
        var keyResponseTypes = allEndpoints.Select(static (endpoints, _) =>
            Transforms.MutationKeyResponseManifestTypeBuilder.Build(endpoints));
        var generatedTypes = programmaticTypes is null
            ? keyResponseTypes
            : programmaticTypes.Value.Combine(keyResponseTypes)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        // One node builds the model; the generated file and the host-local entry both hang off it, so
        // the DTO symbol walking happens once and the two cannot describe different manifests.
        var manifestModel = manifestInput.Combine(generatedTypes).Select(static (data, _) =>
            Manifest.ManifestFeature.BuildManifest(
                data.Left.Left.Left, data.Left.Left.Right, data.Right,
                data.Left.Right.AsImmutableArray()));

        context.RegisterSourceOutputSafe(manifestModel, static (ctx, model) =>
            Manifest.ManifestFeature.Emit(ctx, model));

        // An internationalization value type on this assembly's wire is a declaration of its own: the
        // host installs its JSON converters even where nothing else asked for i18n. Read off
        // the manifest because that model is the wire closure, walked once, for the client generator.
        var i18nOnTheWire = manifestModel.Select(static (model, _) =>
            Manifest.I18nWireTypes.AreOnTheWire(model));

        context.RegisterSourceOutputSafe(i18nOnTheWire, static (ctx, declares) =>
        {
            if (declares)
                ctx.AddSource(new Manifest.Templates.I18nWireTypesMetadataTemplate().RenderOutput());
        });

        // A response the server may keep for any caller is a declaration too: the host adds the output
        // cache for it, without which CacheOutput on the route keeps nothing.
        var sharesResponses = allEndpoints.Select(static (endpoints, _) =>
            endpoints.Any(static e => e.ResponseCache is { IsShared: true }));

        context.RegisterSourceOutputSafe(sharesResponses, static (ctx, declares) =>
        {
            if (declares)
                ctx.AddSource(new Templates.OutputCacheMetadataTemplate().RenderOutput());
        });

        // Generate assembly-level endpoint registration (MapPragmaticEndpoints + AddPragmaticEndpoints)
        // Combine with features to detect host mode for ASP.NET Core auto-wiring
        var endpointsWithFeatures = allEndpoints.Combine(features);
        context.RegisterSourceOutputSafe(endpointsWithFeatures, static (ctx, pair) =>
            GenerateEndpointRegistration(ctx, pair.Left, pair.Right.IsHostMode));

        var manifestRegistrations = manifestModel.Combine(compilationInfo)
            .Select(static (pair, _) => LocalManifestRegistrations(pair.Left, pair.Right.HasComposition));

        // The host's own compilation never sees its own assembly attribute, so the same fact is handed
        // to it directly — the second half of the channel, as every other category does it.
        var i18nWireRegistrations = i18nOnTheWire.Combine(compilationInfo)
            .Select(static (pair, _) => pair.Left && pair.Right.HasComposition
                ? new EquatableArray<Composition.Models.MetadataEntry>(ImmutableArray.Create(
                    Composition.Models.HostLocalRegistration.CreatePayload(
                        Composition.MetadataCategoryIds.I18nWireTypes,
                        "1.0",
                        Manifest.Templates.I18nWireTypesMetadataTemplate.Payload())))
                : EquatableArray<Composition.Models.MetadataEntry>.Empty);

        var outputCacheRegistrations = sharesResponses.Combine(compilationInfo)
            .Select(static (pair, _) => pair.Left && pair.Right.HasComposition
                ? new EquatableArray<Composition.Models.MetadataEntry>(ImmutableArray.Create(
                    Composition.Models.HostLocalRegistration.CreatePayload(
                        Composition.MetadataCategoryIds.OutputCache,
                        "1.0",
                        Templates.OutputCacheMetadataTemplate.Payload())))
                : EquatableArray<Composition.Models.MetadataEntry>.Empty);

        return (allEndpoints,
            endpointsWithCompilation.Select(static (input, _) => LocalEndpointRegistrations(input))
                .Combine(manifestRegistrations)
                .Select(static (pair, _) => new EquatableArray<Composition.Models.MetadataEntry>(
                    pair.Left.AsImmutableArray().AddRange(pair.Right.AsImmutableArray())))
                .Combine(i18nWireRegistrations)
                .Select(static (pair, _) => new EquatableArray<Composition.Models.MetadataEntry>(
                    pair.Left.AsImmutableArray().AddRange(pair.Right.AsImmutableArray())))
                .Combine(outputCacheRegistrations)
                .Select(static (pair, _) => new EquatableArray<Composition.Models.MetadataEntry>(
                    pair.Left.AsImmutableArray().AddRange(pair.Right.AsImmutableArray()))),
            allDerivedPermissions);
    }

    /// <summary>
    ///     Predicate that accepts partial class or partial record declarations.
    ///     Records are needed for DTO-style mutations (partial record : Mutation&lt;T&gt;).
    /// </summary>
    private static bool IsPartialClass(SyntaxNode node, CancellationToken ct)
    {
        return node is TypeDeclarationSyntax typeDecl and (ClassDeclarationSyntax or RecordDeclarationSyntax) &&
               typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
    }

    /// <summary>
    ///     Predicate that accepts property declarations.
    /// </summary>
    private static bool IsPropertyNode(SyntaxNode node, CancellationToken ct)
    {
        return node is PropertyDeclarationSyntax;
    }

    /// <summary>
    ///     Reports diagnostics for invalid endpoint models.
    /// </summary>
    private static void ReportDiagnostics(SourceProductionContext context, EndpointModel model)
    {
        if (model.Location is null)
            return;

        // PRAG9001: the type this endpoint answers with does not resolve, so nothing was generated for
        // it. Only when it is not a domain action — an action's own transform reports the same cause,
        // and one missing using deserves one line rather than two.
        if (model is { UnresolvedResponseType: { } unresolved, IsDomainAction: false })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SourceGenerator.Diagnostics.GenerationDiagnostics.UnresolvedTypeStoppedGeneration, model.Location,
                model.TypeName, unresolved));
        }

        // PRAG0537: the status the error declares and the one it answers with disagree.
        foreach (var error in model.ErrorTypes.Where(e => e.ContradictedStatusCode.HasValue))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.DeclaredStatusContradictsTheError, model.Location,
                error.SimpleName, error.StatusCode, error.ContradictedStatusCode!.Value));
        }

        // PRAG0555: the response keeps the serializer, and which part of its type decided it.
        if (model.ResponseWriterRefusal is { } refusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.ResponseKeepsTheSerializer, model.Location, model.TypeName, refusal));
        }

        // PRAG0527: a field the generator cannot classify is not injected — say so.
        foreach (var ambiguous in model.AmbiguousDependencies)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.DependencyTypeAmbiguous, model.Location,
                ambiguous.FieldName, model.TypeName, ambiguous.TypeName));
        }

        switch (model.InvalidReason)
        {
            // NotPartial: PRAG0500 is the companion analyzer's.

            case InvalidReason.NotEndpoint:
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.MustInheritFromEndpoint,
                    model.Location,
                    model.TypeName));
                break;

            case InvalidReason.MissingRoute:
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.RouteRequired,
                    model.Location,
                    model.TypeName));
                break;

            case InvalidReason.TooManyErrors:
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.TooManyErrorTypes,
                    model.Location,
                    model.TypeName,
                    model.ErrorTypes.Length));
                break;
        }

        // PRAG0531: a declared response DTO the entity cannot be mapped into. Reported here rather than
        // left to the compiler, which would fail on a missing FromEntity inside a generated file.
        if (model.ReturnsDtoWithoutMapFrom is { } dto)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.ReturnsDtoCannotMapFromEntity,
                model.Location,
                dto, model.TypeName, model.MutationEntityType?.Replace("global::", "") ?? "the entity"));
        }

        // PRAG0535: a declared DTO beside a key ReturnType. The key answers, and without this the DTO
        // would be dropped without a word — the author gets {"id": …} where they wrote the DTO.
        if (model.ReturnsDtoBesideKeyResponse is { } deadDto)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.ReturnsDtoBesideAKeyReturnType,
                model.Location,
                deadDto,
                model.TypeName,
                model.MutationReturnsId ? "Id" : "LogicalKey",
                model.MutationReturnsId ? "id" : "logical key"));
        }

        // PRAG0533: a create answering through a navigation. Without it the request fails with a 500
        // naming three remedies, without saying that the first one — [EagerLoad] — has no query to
        // attach to on a create.
        if (model.CreateResponseNavigatesTo is { } navigated)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.CreateCannotReturnANavigatedDto,
                model.Location,
                model.MutationResponseFactory?.Replace(".FromEntity", "").Replace("global::", "")
                    ?? "the response DTO",
                model.TypeName,
                navigated));
        }

        // PRAG0532: a nested object on an operation exposed as GET. A GET has no body and a query string
        // carries scalars, so this value has nowhere to travel — said here rather than dropped, which
        // would leave a working endpoint quietly ignoring a parameter.
        if (model.IsValid)
        {
            foreach (var unbindable in model.UnbindableOnQueryString)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.GetCannotCarryComplexProperty,
                    model.Location,
                    model.TypeName,
                    unbindable.Name,
                    unbindable.TypeName.Replace("global::", "")));
            }
        }

        // PRAG0552: the same question for a multipart request, which has no JSON body either — every
        // value travels as a form field, and a form field cannot carry a nested object. Its scalars are
        // bound from the form whether or not [FromForm] is written on them.
        if (model.IsValid)
        {
            foreach (var unbindable in model.UnbindableOnMultipart)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.MultipartCannotCarryComplexProperty,
                    model.Location,
                    model.TypeName,
                    unbindable.Name,
                    unbindable.TypeName.Replace("global::", "")));
            }
        }

        // PRAG0504: Route parameters that don't match any public property
        if (model is { IsValid: true, UnmatchedRouteParameters.Length: > 0 })
        {
            foreach (var unmatched in model.UnmatchedRouteParameters)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.RouteParameterNotFound,
                    model.Location,
                    unmatched.Name,
                    model.TypeName,
                    unmatched.ExpectedPropertyName));
            }
        }

        // PRAG0551: Versioned Execute methods detected but Asp.Versioning.Http not referenced
        if (model is { IsValid: true, HasActionVersioning: true, HasAspVersioning: false })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.VersioningRequiresAspVersioning,
                model.Location,
                model.TypeName));
        }

        // A shared cache on a route that needs a caller: the output cache never keeps an authenticated
        // request, so the attribute keeps nothing (PRAG0554).
        if (model is { IsValid: true, IsStreamingResponse: false, ResponseCache.IsShared: true }
            && model.Authorization is not { AllowAnonymous: true })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.SharedResponseCacheOnAuthenticatedEndpoint, model.Location, model.TypeName));
        }

        // Streaming (SSE) constraints: PRAG0520-0524
        if (model is { IsValid: true, IsStreamingResponse: true })
        {
            if (model.ResponseCache is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.ResponseCacheOnStreaming, model.Location, model.TypeName));

            if (model.HttpMethod is not ("Get" or "Post"))
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.StreamingVerbInvalid, model.Location, model.TypeName,
                    model.HttpMethod.ToUpperInvariant()));

            if (model.SuccessStatusCode is not null || model.CreatedAtTemplate is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.StatusOverrideOnStreaming, model.Location, model.TypeName));

            if (model.ActionVersions.Length > 1)
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.VersioningOnStreaming, model.Location, model.TypeName));

            if (!model.PostProcessors.IsDefaultOrEmpty)
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.PostProcessorOnStreaming, model.Location, model.TypeName));
        }

        // PRAG0513: [Idempotent] on safe verbs is meaningless — the filter is not emitted
        if (model is { IsValid: true, Idempotency: not null, HttpMethod: "Get" or "Head" or "Options" })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.IdempotentOnSafeVerb,
                model.Location,
                model.TypeName,
                model.HttpMethod.ToUpperInvariant()));
        }

        // PRAG0517: [MaxBodySize] must be positive; non-positive limits are not emitted
        if (model is { IsValid: true, MaxBodySizeBytes: <= 0 })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.InvalidMaxBodySize,
                model.Location,
                model.TypeName,
                model.MaxBodySizeBytes));
        }

        // PRAG0516: [MaxFileSize] must be positive; a non-positive limit rejects every upload
        if (model.IsValid)
        {
            foreach (var fileParam in model.FormParameters)
                if (fileParam.MaxFileSize is <= 0)
                    context.ReportDiagnostic(Diagnostic.Create(
                        EndpointsDiagnostics.InvalidMaxFileSize,
                        model.Location,
                        model.TypeName,
                        fileParam.MaxFileSize));
        }

        // PRAG0518: invalid JSON in [RequestExample]/[ResponseExample]
        if (model.IsValid)
        {
            foreach (var example in model.RequestExamples)
                if (!example.IsValidJson)
                    context.ReportDiagnostic(Diagnostic.Create(
                        EndpointsDiagnostics.InvalidExampleJson,
                        model.Location,
                        model.TypeName,
                        Truncate(example.Json)));

            foreach (var example in model.ResponseExamples)
                if (!example.IsValidJson)
                    context.ReportDiagnostic(Diagnostic.Create(
                        EndpointsDiagnostics.InvalidExampleJson,
                        model.Location,
                        model.TypeName,
                        Truncate(example.Json)));

            static string Truncate(string json)
                => json.Length <= 60 ? json : json.Substring(0, 57) + "...";
        }

        // PRAG0514: HEAD must not carry a response body (RFC 9110) — the generated handler suppresses it
        if (model is { IsValid: true, HttpMethod: "Head", IsVoid: false })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.HeadEndpointHasResponseBody,
                model.Location,
                model.TypeName));
        }

        // PRAG0507: [EndpointGroup<X>] where X does not exist, or exists without [EndpointGroup]. The
        // same id for both — the remedy is the same attribute — and a message that says which.
        if (model is { IsValid: true, Group.IsUnusable: true })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EndpointsDiagnostics.GroupNotFound,
                model.Location,
                model.Group.TypeName.Replace("global::", ""),
                model.TypeName,
                model.Group.NotFound ? "does not exist" : "is not decorated with [EndpointGroup]"));
        }

        // PRAG0534: a declared processor the container cannot construct. The registration leaves it
        // out — there is no line that would work — so without this the endpoint would answer 500 on
        // its first call instead of failing the build.
        if (model.IsValid)
        {
            foreach (var processor in model.PreProcessors.AsImmutableArray()
                         .Concat(model.PostProcessors.AsImmutableArray())
                         .Where(p => p.NotConstructibleReason is not null))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.ProcessorNotConstructible,
                    model.Location,
                    processor.TypeName.Replace("global::", ""),
                    processor.NotConstructibleReason));
            }
        }

        // PRAG0536: an optional init value whose initializer cannot be repeated in the generated
        // initializer. The handler binds nothing for it rather than emitting CS8852 into its own file.
        if (model.IsValid)
        {
            var unreproducible = model.HeaderParameters
                .Where(p => p is { IsRequired: false, IsInitOnly: true, InitOnlyFallback: null })
                .Select(p => p.PropertyName)
                .Concat(model.QueryParameters
                    .Where(p => p is { IsRequired: false, IsInitOnly: true, InitOnlyFallback: null, IsComplexFilter: false })
                    .Select(p => p.PropertyName))
                .Concat(model.ClaimParameters
                    .Where(p => p is { IsRequired: false, IsInitOnly: true, InitOnlyFallback: null })
                    .Select(p => p.PropertyName))
                .Concat(model.CookieParameters
                    .Where(p => p is { IsRequired: false, IsInitOnly: true, InitOnlyFallback: null })
                    .Select(p => p.PropertyName));

            foreach (var property in unreproducible)
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.OptionalInitValueWithoutConstantDefault,
                    model.Location,
                    property,
                    model.TypeName));
        }

        // PRAG0512: Info when 5+ body properties implicitly bind without explicit attribute
        if (model is { IsValid: true, ImplicitBodyPropertyCount: >= 5 })
        {
            foreach (var bodyProp in model.BodyProperties)
            {
                if (bodyProp.IsImplicit)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        EndpointsDiagnostics.ImplicitBodyProperty,
                        model.Location,
                        bodyProp.Name,
                        model.TypeName));
                }
            }
        }
    }

    /// <summary>
    ///     Reports diagnostics for invalid autocomplete models.
    /// </summary>
    private static void ReportAutocompleteDiagnostics(SourceProductionContext context, AutocompleteModel model)
    {
        if (model.IsValid || model.Location is null)
            return;

        switch (model.InvalidReason)
        {
            case AutocompleteInvalidReason.MissingKeyProperty:
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.AutocompleteMissingKey,
                    model.Location,
                    model.PropertyName,
                    model.EntityTypeName));
                break;
            case AutocompleteInvalidReason.NotStringProperty:
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.AutocompleteRequiresStringProperty,
                    model.Location,
                    model.EntityTypeName,
                    model.PropertyName,
                    "non-string"));
                break;
        }
    }
}
