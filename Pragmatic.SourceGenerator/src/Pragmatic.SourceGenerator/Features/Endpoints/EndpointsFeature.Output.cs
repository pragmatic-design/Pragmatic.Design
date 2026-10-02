using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Diagnostics;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Templates;

namespace Pragmatic.SourceGenerator.Features.Endpoints;

internal static partial class EndpointsFeature
{
    /// <summary>
    ///     Generates the endpoint handler partial class and related files.
    /// </summary>
    private static void GenerateEndpointHandler(SourceProductionContext context, EndpointModel model)
    {
        // Generate the MapEndpoint handler (different template per base type)
        if (model.IsQuery)
        {
            var queryTemplate = new QueryHandlerTemplate(model);
            var queryArtifact = queryTemplate.RenderOutput();
            if (!queryArtifact.IsEmpty)
                context.AddSource(queryArtifact);
        }
        else if (model.IsMutation)
        {
            var mutationTemplate = new MutationHandlerTemplate(model);
            var mutationArtifact = mutationTemplate.RenderOutput();
            if (!mutationArtifact.IsEmpty)
                context.AddSource(mutationArtifact);
        }
        else if (model.IsDomainAction)
        {
            var domainActionTemplate = new DomainActionHandlerTemplate(model);
            var domainActionArtifact = domainActionTemplate.RenderOutput();
            if (!domainActionArtifact.IsEmpty)
                context.AddSource(domainActionArtifact);
        }
        else
        {
            var handlerTemplate = new EndpointHandlerTemplate(model);
            var handlerArtifact = handlerTemplate.RenderOutput();
            if (!handlerArtifact.IsEmpty)
                context.AddSource(handlerArtifact);

            // Generate SetDependencies if endpoint has dependencies (standalone only)
            if (model.HasDependencies)
            {
                var dependenciesTemplate = new SetDependenciesTemplate(model);
                var dependenciesArtifact = dependenciesTemplate.RenderOutput();
                if (!dependenciesArtifact.IsEmpty)
                    context.AddSource(dependenciesArtifact);
            }
        }

        // Generate body DTO if endpoint has body properties
        if (model.NeedsBodyDto)
        {
            if (model is { HasActionVersioning: true, HasAspVersioning: true })
            {
                // Generate versioned body DTOs (one per version)
                var versionedTemplate = new VersionedBodyDtoTemplate(model);
                var versionedArtifact = versionedTemplate.RenderOutput();
                if (!versionedArtifact.IsEmpty)
                    context.AddSource(versionedArtifact);
            }
            else
            {
                var bodyDtoTemplate = new BodyDtoTemplate(model);
                var bodyDtoArtifact = bodyDtoTemplate.RenderOutput();
                if (!bodyDtoArtifact.IsEmpty)
                    context.AddSource(bodyDtoArtifact);
            }
        }
    }

    /// <summary>
    ///     Generates the autocomplete endpoint handler class.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Reports nothing about authentication. The module cannot know whether the host
    ///     authenticates, and saying it here would pin <c>Identity.AspNetCore</c> to boundary libraries
    ///     that need nothing from it. The fact travels instead, on the endpoint's
    ///     <c>DerivedPermission</c>, and the host says it where it has an answer: <b>PRAG1692</b>, for
    ///     the one case its own PRAG1695 does not already cover.
    /// </remarks>
    private static void GenerateAutocompleteEndpoint(SourceProductionContext context, AutocompleteModel model)
        => context.AddSource(new AutocompleteEndpointTemplate(model).RenderOutput());

    /// <summary>
    ///     Generates assembly-level metadata for endpoint discovery (enriched JSON).
    /// </summary>
    private static void GenerateEndpointMetadata(
        SourceProductionContext context,
        (ImmutableArray<EndpointModel> Endpoints, (bool IsDebug, bool HasComposition) Info) input)
    {
        var (endpoints, (isDebug, hasComposition)) = input;

        if (!hasComposition || endpoints.IsEmpty)
            return;

        var template = new EndpointMetadataTemplate(endpoints, isDebug);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     The same document <see cref="GenerateEndpointMetadata" /> writes, handed to Composition for a
    ///     host that declares its own <c>[Endpoint]</c> types.
    /// </summary>
    /// <remarks>
    ///     The host renders <c>MapEndpoint(root)</c> per endpoint inline from this payload — the
    ///     Endpoints metadata declares <c>"registrationMethod": null</c> and there is no generated
    ///     <c>Add*</c> for it to call instead. Mirrors the generation condition exactly.
    /// </remarks>
    private static EquatableArray<Composition.Models.MetadataEntry> LocalEndpointRegistrations(
        (ImmutableArray<EndpointModel> Endpoints, (bool IsDebug, bool HasComposition) Info) input)
    {
        var (endpoints, (isDebug, hasComposition)) = input;

        if (!hasComposition || endpoints.IsEmpty)
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        return ImmutableArray.Create(
            Composition.Models.HostLocalRegistration.CreatePayload(
                Composition.MetadataCategoryIds.Endpoints,
                MetadataSchemaVersions.Endpoints,
                new EndpointMetadataTemplate(endpoints, isDebug).BuildJson()));
    }

    /// <summary>
    ///     The same manifest document <c>ManifestJsonTemplate</c> writes into
    ///     <c>[assembly: PragmaticMetadata]</c>, handed to Composition for a host that declares its own
    ///     module.
    /// </summary>
    /// <remarks>
    ///     Without it <c>GenerateAggregatedManifest</c> would have nothing to merge for such a host and
    ///     would emit neither the aggregated manifest nor the compile-time OpenAPI document — so runtime
    ///     OpenAPI enrichment would have no source to read and would silently do nothing.
    /// </remarks>
    private static EquatableArray<Composition.Models.MetadataEntry> LocalManifestRegistrations(
        Manifest.Models.ManifestModel? manifest,
        bool hasComposition)
    {
        if (!hasComposition || manifest is null)
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        // Same gate the template validates on: an artifact whose Validate() fails is never written, and
        // an entry for a document nobody emitted would put an empty manifest into the aggregation.
        var template = new Manifest.Templates.ManifestJsonTemplate(manifest);
        if (!template.HasContent)
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        return ImmutableArray.Create(
            Composition.Models.HostLocalRegistration.CreatePayload(
                Composition.MetadataCategoryIds.Manifest,
                "1.0.0",
                template.BuildCompactJson()));
    }

    /// <summary>
    ///     Generates assembly-level endpoint contract attributes for host-SG / tooling consumption.
    ///     One <c>[PragmaticEndpointContract]</c> attribute per endpoint.
    /// </summary>
    private static void GenerateEndpointContracts(
        SourceProductionContext context,
        ImmutableArray<EndpointModel> endpoints)
    {
        if (endpoints.IsEmpty)
            return;

        var template = new EndpointContractTemplate(endpoints);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     Generates ApiRoutes: compile-time route constants + typed URL builders per boundary.
    ///     Reports PRAG0526 when two endpoints in a boundary collapse to the same member name.
    /// </summary>
    private static void GenerateApiRoutes(
        SourceProductionContext context,
        (ImmutableArray<EndpointModel> Endpoints, string AssemblyName) input)
    {
        var (endpoints, assemblyName) = input;
        if (endpoints.IsEmpty)
            return;

        // PRAG0505: an explicit [Endpoint(Name = "...")] must be unique across the assembly — endpoint
        // names drive link generation. Endpoints without an explicit Name (null/empty) never collide.
        foreach (var nameGroup in endpoints
                     .Where(e => e.IsValid && !string.IsNullOrEmpty(e.Name))
                     .GroupBy(e => e.Name!, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            foreach (var duplicate in nameGroup.OrderBy(e => e.TypeName, StringComparer.Ordinal).Skip(1))
                if (duplicate.Location is not null)
                    context.ReportDiagnostic(Diagnostic.Create(
                        EndpointsDiagnostics.DuplicateEndpointName, duplicate.Location, duplicate.Name));
        }

        // PRAG0529: two endpoints on the same verb and route. Routing cannot choose between them and
        // answers 500 per request, which is the worst place to find out: the build is clean and the
        // OpenAPI document lists the route once.
        foreach (var routeGroup in endpoints
                     .Where(e => e.IsValid)
                     // The full route, not the declared one: two endpoints both writing "/{id}" are
                     // ordinary when their groups prefix them differently, and comparing what the author
                     // typed reported every one of those as a collision.
                     .GroupBy(
                         e => e.HttpMethod + " " + Routes.EndpointRouteFacts.FullRoute(e),
                         StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            var ordered = routeGroup.OrderBy(e => e.TypeName, StringComparer.Ordinal).ToList();

            // A collision needs one survivor, or an ordinary duplicate would be reported from both
            // sides. Prefer a generated endpoint for the role: it carries no location, so it could
            // never be reported anyway, and it is not a line anyone can edit.
            //
            // Reporting on Skip(1) instead lost the collision whenever the generated endpoint sorted
            // later — which "Resource…" does against most hand-written names — because the guard below
            // then skipped the only candidate. Most of those never reach here: the stand-down rule in
            // Register() already drops a scaffolded endpoint whose verb and route a hand-written one
            // claims. It compares the route **as declared**, though, and this compares it **as served**,
            // so an endpoint inside an [EndpointGroup] declares "guests" where the scaffolded one
            // declares "/api/booking/guests" — no overlap there, one path here, and 500 per request in
            // the application. See DuplicateRouteThroughAGroupTests.
            var survivor = ordered.FindIndex(e => e.Location is null);
            if (survivor < 0)
                survivor = 0;

            for (var i = 0; i < ordered.Count; i++)
            {
                if (i == survivor)
                    continue;

                var duplicate = ordered[i];
                if (duplicate.Location is null)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.DuplicateRoute, duplicate.Location,
                    duplicate.TypeName, duplicate.HttpMethod,
                    Routes.EndpointRouteFacts.FullRoute(duplicate), ordered[survivor].TypeName));
            }
        }

        // PRAG0526: name collisions per boundary (the template keeps the first, skips the rest)
        foreach (var group in endpoints.Where(e => e.IsValid)
                     .GroupBy(e => Routes.EndpointRouteFacts.Boundary(e) ?? "Api"))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ep in group.OrderBy(e => e.TypeName, StringComparer.Ordinal))
            {
                var name = Routes.EndpointRouteFacts.OperationName(ep);
                if (!seen.Add(name) && ep.Location is not null)
                    context.ReportDiagnostic(Diagnostic.Create(
                        EndpointsDiagnostics.ApiRouteNameCollision, ep.Location, name, group.Key));
            }
        }

        var template = new Routes.Templates.ApiRoutesTemplate(endpoints, assemblyName);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     Generates assembly-level endpoint registration (MapPragmaticEndpoints + AddPragmaticEndpoints).
    /// </summary>
    private static void GenerateEndpointRegistration(
        SourceProductionContext context,
        ImmutableArray<EndpointModel> endpoints,
        bool isHostMode)
    {
        if (endpoints.IsEmpty)
            return;

        var template = new EndpointRegistrationTemplate(endpoints, isHostMode);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }
}
