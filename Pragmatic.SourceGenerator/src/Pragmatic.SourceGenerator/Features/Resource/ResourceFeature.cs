using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Resource.Diagnostics;
using Pragmatic.SourceGenerator.Features.Resource.Models;
using Pragmatic.SourceGenerator.Features.Resource.Templates;
using Pragmatic.SourceGenerator.Features.Resource.Transforms;

namespace Pragmatic.SourceGenerator.Features.Resource;

/// <summary>
/// Registers the [Resource] attribute pipeline.
/// Returns the collected resource models for downstream features (Traits, CRUD).
/// </summary>
internal static class ResourceFeature
{
    private static readonly Regex KebabCasePattern = new(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.Compiled);

    private const string ReturnsDtoAttribute = "Pragmatic.Persistence.Entity.ReturnsDtoAttribute`1";

    /// <summary>
    ///     The attribute-only partial parts a developer writes to decorate a scaffolded operation.
    /// </summary>
    private static IncrementalValuesProvider<Models.ResourceOverrideModel> DecoratedParts(
        IncrementalGeneratorInitializationContext context, string attributeName)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: ResourceOverrideTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

    public static (IncrementalValueProvider<ImmutableArray<ResourceModel>> Models,
        IncrementalValueProvider<ImmutableArray<Persistence.Models.QueryModel>> Queries,
        IncrementalValueProvider<ImmutableArray<EndpointModel>> Endpoints,
        IncrementalValueProvider<ImmutableArray<Actions.Models.MutationModel>> Mutations,
        IncrementalValueProvider<ImmutableArray<Mapping.Models.MappingModel>> Mappings,
        IncrementalValueProvider<ImmutableArray<Manifest.Models.ManifestTypeModel>> ManifestTypes) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var resourceModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Resource,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: ResourceTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.ResourceResources);

        var allResources = resourceModels.Collect();

        // Partial parts a developer writes to decorate a scaffolded operation. Attributes on a partial
        // class combine across its parts, so the body stays generated and the decoration is theirs —
        // but only the compiler sees that. The generator builds the scaffolded models from the entity
        // and would never look at the developer's file, so it is collected here and matched by type
        // identity below.
        // One provider per attribute — that is how ForAttributeWithMetadataName works — but one
        // transform: it reads everything off the symbol, so a part carrying both attributes produces the
        // same model twice and Index() collapses it.
        var overrides = DecoratedParts(context, Endpoints.EndpointAttributeNames.RequirePermission).Collect()
            .Combine(DecoratedParts(context, ReturnsDtoAttribute).Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right))
            .WithTrackingName(TrackingNames.ResourceOverrides);

        // Validate aggregate constraints (duplicate segments)
        context.RegisterSourceOutputSafe(allResources, static (ctx, resources) =>
        {
            if (resources.IsDefaultOrEmpty) return;
            Validate(ctx, resources);
        });

        // The raw Compilation is genuinely needed ONCE: ResourceCrudTransform resolves the entity
        // SYMBOL and walks its property hierarchy, which no scalar projection can preserve. Building
        // the CRUD models here — instead of re-combining the Compilation in each of the four
        // downstream stages — means this is the only node that re-runs on every edit; the value
        // comparer below keeps DTO/query/endpoint/action generation cached when nothing changed.
        var crudModels = allResources
            .Combine(context.CompilationProvider)
            .Select(static (pair, ct) =>
            {
                var (resources, compilation) = pair;

                // Once for the compilation, not once per resource: which group each entity's
                // hand-written operations are in, so the scaffolding can join them.
                var operationNamespaceByEntity = Transforms.EntityGroupReader.Read(compilation, ct);

                var builder = ImmutableArray.CreateBuilder<ResourceCrudModel>();
                foreach (var resource in resources)
                {
                    if (resource.Capabilities == 0) continue;
                    var crudModel = ResourceCrudTransform.Build(resource, compilation, operationNamespaceByEntity);
                    if (crudModel is null) continue;
                    builder.Add(crudModel);
                }

                return builder.ToImmutable();
            })
            .WithComparer(ImmutableArraySequenceComparer<ResourceCrudModel>.Instance)
            .WithTrackingName(TrackingNames.ResourceCrudModels);

        // The developer's [ReturnsDto<T>] is folded into the model here, once. Every stage below names a
        // DTO — the query attribute, the query model, the endpoint, the mapping, the manifest — and
        // resolving the override in each of them would be five copies of one rule, free to disagree.
        // The shape a query declares and the shape it projects disagreeing is not a compile error: it
        // is an empty result at runtime.
        crudModels = crudModels
            .Combine(overrides)
            .Select(static (pair, _) => WithDeclaredDtos(pair.Left, pair.Right))
            .WithComparer(ImmutableArraySequenceComparer<ResourceCrudModel>.Instance)
            .WithTrackingName(TrackingNames.ResourceResolvedCrudModels);

        // CRUD DTO + Action + Query generation for resources with Capabilities
        context.RegisterSourceOutputSafe(
            crudModels.SelectMany(static (models, _) => models),
            static (ctx, crudModel) => GenerateCrud(ctx, crudModel));

        // PRAG2608/2609: a declared DTO the query cannot project into. Reported here rather than left
        // to runtime because the failure is silent — the executor falls back to OfType<TResult>(), which
        // matches nothing and answers 200 with an empty body.
        context.RegisterSourceOutputSafe(crudModels.Combine(overrides),
            static (ctx, pair) => ReportUnprojectableDtos(ctx, pair.Left, pair.Right));

        // Build QueryModels for QueryFeature injection
        var resourceQueryModels = crudModels
            .Select(static (models, _) =>
            {
                var builder = ImmutableArray.CreateBuilder<Persistence.Models.QueryModel>();

                foreach (var crudModel in models)
                {
                    var capabilities = crudModel.Resource.Capabilities;
                    if ((capabilities & CapSearch) != 0)
                        builder.Add(ResourceQueryTemplate.BuildSearchQueryModel(crudModel));
                    if ((capabilities & CapList) != 0)
                        builder.Add(ResourceQueryTemplate.BuildListQueryModel(crudModel));

                    if ((capabilities & CapRead) != 0)
                    {
                        builder.Add(ResourceQueryTemplate.BuildReadQueryModel(crudModel));
                        if (crudModel.LogicKeyName is not null)
                            builder.Add(ResourceQueryTemplate.BuildReadByLogicKeyQueryModel(crudModel));
                    }
                }

                return builder.ToImmutable();
            })
            .WithTrackingName(TrackingNames.ResourceQueries);

        // Build EndpointModels for EndpointsFeature injection, applying whatever a developer declared
        // on the matching partial part. Theirs replaces the scaffolded default rather than adding to
        // it — see ResourceOverrideModel.
        var resourceEndpointModels = crudModels
            .Combine(overrides)
            .Select(static (pair, _) =>
            {
                var (models, declared) = pair;
                var index = ResourceOverrideTransform.Index(declared);
                var builder = ImmutableArray.CreateBuilder<EndpointModel>();

                foreach (var crudModel in models)
                foreach (var endpoint in ResourceEndpointModelBuilder.Build(crudModel))
                    builder.Add(ResourceOverrideTransform.Apply(endpoint, index));

                return builder.ToImmutable();
            })
            .WithTrackingName(TrackingNames.ResourceEndpoints);

        // PRAG2607: a decorated declaration that matches nothing. Silent otherwise: an empty class of
        // the developer's own, and the default they meant to replace still in force.
        context.RegisterSourceOutputSafe(
            resourceEndpointModels.Combine(overrides),
            static (ctx, pair) => ReportUnmatchedOverrides(ctx, pair.Left, pair.Right));

        // The write capabilities, as mutations. Injected into ActionsFeature so they reach every stage
        // a hand-written mutation reaches — validation, permissions, commit strategy, event hand-over —
        // instead of a bare repository call of their own.
        var resourceMutationModels = crudModels
            .Select(static (models, _) =>
            {
                var builder = ImmutableArray.CreateBuilder<Actions.Models.MutationModel>();
                foreach (var crudModel in models)
                    builder.AddRange(Transforms.ResourceMutationModelBuilder.Build(crudModel));
                return builder.ToImmutable();
            })
            .WithTrackingName(TrackingNames.ResourceMutations);

        // The declaration itself. ActionsFeature generates everything inside it from the model above.
        // Emitted from the CRUD model rather than the mutation models, so the declaration can state
        // what a developer's own partial part decided about it — see ResourceDecorationRemark.
        context.RegisterSourceOutputSafe(
            crudModels.SelectMany(static (models, _) => models),
            static (ctx, crudModel) =>
            {
                foreach (var model in Transforms.ResourceMutationModelBuilder.Build(crudModel))
                {
                    ctx.AddSource(new Templates.ResourceMutationTemplate(
                        model, model.Mode.ToString(), crudModel.DecorationOf(model.TypeName)).RenderOutput());
                }
            });

        // The read DTOs, as mapping models. MappingFeature renders their FromEntity and Projection —
        // it is syntax-driven and cannot see a type this generator creates, so without this the same
        // mapping was written a second time here, free to drift from the one everyone else gets.
        var resourceMappingModels = crudModels
            .Select(static (models, _) =>
            {
                var builder = ImmutableArray.CreateBuilder<Mapping.Models.MappingModel>();
                foreach (var crudModel in models)
                    builder.AddRange(Transforms.ResourceMappingModelBuilder.Build(crudModel));
                return builder.ToImmutable();
            })
            .WithTrackingName(TrackingNames.ResourceMappings);

        // Descriptions of the four generated DTOs, for the manifest: it cannot resolve a type this
        // generator is creating, so the shape is contributed from the same model the templates render.
        var resourceManifestTypes = crudModels.Select(static (models, _) =>
            Transforms.ResourceManifestTypeBuilder.Build(models));

        return (allResources, resourceQueryModels, resourceEndpointModels,
            resourceMutationModels, resourceMappingModels, resourceManifestTypes);
    }

    // ResourceCapabilities flags
    private const int CapCreate = 1;
    private const int CapRead = 2;
    private const int CapUpdate = 4;
    private const int CapDelete = 8;
    private const int CapList = 16;
    private const int CapSearch = 32;

    /// <summary>
    ///     Folds the developer's <c>[ReturnsDto&lt;T&gt;]</c> declarations into the models the templates
    ///     and the injected models are all built from.
    /// </summary>
    private static ImmutableArray<ResourceCrudModel> WithDeclaredDtos(
        ImmutableArray<ResourceCrudModel> models, ImmutableArray<Models.ResourceOverrideModel> declared)
    {
        if (models.IsDefaultOrEmpty || declared.IsDefaultOrEmpty)
            return models;

        var byOperation = ImmutableDictionary.CreateBuilder<string, Models.ResourceOverrideModel>(
            StringComparer.Ordinal);
        foreach (var model in declared)
        {
            if (model.HasDeclaration)
                byOperation[model.Key] = model;
        }

        if (byOperation.Count == 0)
            return models;

        var builder = ImmutableArray.CreateBuilder<ResourceCrudModel>(models.Length);
        foreach (var model in models)
        {
            // Only the declarations that name one of THIS resource's operations. The provider that
            // feeds this sees every decorated part in the assembly, so without the filter one entity's
            // model would carry another's — and the DTO would silently move to the wrong resource.
            var mine = ImmutableArray.CreateBuilder<Models.ResourceOverrideModel>();
            foreach (var operation in model.Operations)
            {
                if (byOperation.TryGetValue(model.KeyOf(operation), out var decoration))
                    mine.Add(decoration);
            }

            builder.Add(mine.Count == 0 ? model : model with { Decorations = mine.ToImmutable() });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     PRAG2608/2609 — a declared DTO the generated query cannot project into.
    /// </summary>
    private static void ReportUnprojectableDtos(
        SourceProductionContext ctx,
        ImmutableArray<ResourceCrudModel> models,
        ImmutableArray<Models.ResourceOverrideModel> declared)
    {
        if (models.IsDefaultOrEmpty || declared.IsDefaultOrEmpty)
            return;

        foreach (var model in models)
        {
            var entity = model.Resource.FullTypeName.StartsWith("global::", StringComparison.Ordinal)
                ? model.Resource.FullTypeName.Substring("global::".Length)
                : model.Resource.FullTypeName;

            foreach (var decoration in model.Decorations)
            {
                if (decoration.DeclaredDto is not { } dto || decoration.LocationInfo is not { } location)
                    continue;

                if (!dto.HasProjection)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        ResourceDiagnostics.DeclaredDtoHasNoProjection,
                        location.ToLocation(),
                        dto.DtoTypeName, entity));
                }
                else if (dto.MapsFromEntity is not null && dto.MapsFromEntity != entity)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        ResourceDiagnostics.DeclaredDtoMapsFromAnotherEntity,
                        location.ToLocation(),
                        dto.DtoTypeName, dto.MapsFromEntity, entity));
                }
            }
        }
    }

    /// <summary>
    ///     PRAG2607 — a partial part carrying operation attributes that decorates nothing.
    /// </summary>
    /// <remarks>
    ///     Only reported when the assembly scaffolds something: in a project with no <c>[Resource]</c>
    ///     capabilities every <c>[RequirePermission]</c> in sight would otherwise be a false positive,
    ///     since the provider that feeds this is keyed on that attribute and sees them all.
    /// </remarks>
    private static void ReportUnmatchedOverrides(
        SourceProductionContext ctx,
        ImmutableArray<EndpointModel> scaffolded,
        ImmutableArray<Models.ResourceOverrideModel> declared)
    {
        if (scaffolded.IsDefaultOrEmpty || declared.IsDefaultOrEmpty)
            return;

        var keys = ResourceOverrideTransform.KeysOf(scaffolded);

        foreach (var model in declared)
        {
            if (keys.Contains(model.Key) || model.LocationInfo is null)
                continue;

            // Only the operations of the namespace it was written in: naming every scaffolded type in
            // the assembly would bury the one they meant among dozens.
            var candidates = keys
                .Where(k => k.StartsWith(model.Namespace + ".", StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => k.Substring(model.Namespace.Length + 1))
                .ToList();

            if (candidates.Count == 0)
                continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                ResourceDiagnostics.OverrideMatchesNoOperation,
                model.LocationInfo.Value.ToLocation(),
                model.TypeName,
                string.Join(", ", candidates)));
        }
    }

    /// <summary>
    ///     PRAG2610: a capability was asked for that this entity cannot have.
    /// </summary>
    /// <remarks>
    ///     Restore is skipped on an entity that is not <c>[SoftDelete]</c>, which is right — a hard delete
    ///     leaves nothing to restore. Skipping it in silence is not: the author asked for an endpoint and
    ///     got none, with nothing to read. <c>ResourceCapabilities.All</c> is deliberately not reported,
    ///     because there the word means "everything that applies" and no single capability was named.
    /// </remarks>
    private static void ReportInapplicableCapabilities(SourceProductionContext ctx, ResourceCrudModel crudModel)
    {
        var resource = crudModel.Resource;
        if (resource.Location is null || resource.Capabilities == CapabilityAll)
            return;

        if ((resource.Capabilities & CapabilityRestore) != 0 && !crudModel.IsSoftDelete)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                ResourceDiagnostics.CapabilityCannotApply,
                resource.Location,
                resource.TypeName,
                "Restore",
                $"'{resource.TypeName}' is not [SoftDelete], so a delete leaves no row to restore"));
        }
    }

    private const int CapabilityRestore = 64;

    /// <summary>Create|Read|Update|Delete|List|Search|Restore — the value <c>ResourceCapabilities.All</c> has.</summary>
    private const int CapabilityAll = 127;

    private static void GenerateCrud(SourceProductionContext ctx, ResourceCrudModel crudModel)
    {
        var caps = crudModel.Resource.Capabilities;

        ReportInapplicableCapabilities(ctx, crudModel);

        // DTOs — the read shapes only. A mutation is its own input contract, so a CreateDto/UpdateDto
        // described a body that already had a type. And only while something still answers with them:
        // declaring [ReturnsDto<T>] on every read operation leaves the scaffolded shape referenced by
        // nothing, and a generated type nobody names is a type that only has to be maintained.
        //
        // "Something answers with it" is the whole condition, and NeedsScaffoldedReadDto is what says
        // so — a create answers with what it created, an update and a restore with what they left
        // behind, a soft delete with the row it marked. Asking for Read on top of that meant a
        // resource that only creates, or only deletes, named a DTO nobody emitted: a build error
        // inside a file its author cannot open, from a declaration that reads as perfectly ordinary.
        if (crudModel.NeedsScaffoldedReadDto)
            EmitArtifact(ctx, new ResourceDtoTemplate(crudModel, ResourceDtoKind.Read));

        if ((caps & CapList) != 0 && crudModel.NeedsScaffoldedListItemDto)
            EmitArtifact(ctx, new ResourceDtoTemplate(crudModel, ResourceDtoKind.ListItem));

        // Reading one row is a Single query, not an action that calls the repository by hand: it gets
        // the query pipeline's filters and its 404. Create/Update/Delete/Restore are mutations, and
        // their declarations are emitted from the injected MutationModels — see Register.
        if ((caps & CapRead) != 0)
        {
            EmitArtifact(ctx, new ResourceQueryTemplate(crudModel, ResourceQueryKind.Read));
            if (crudModel.LogicKeyName is not null)
                EmitArtifact(ctx, new ResourceQueryTemplate(crudModel, ResourceQueryKind.ReadByLogicKey));
        }

        // Query classes (List/Search) — Apply() generated by QueryFeature from injected QueryModel
        if ((caps & CapList) != 0)
            EmitArtifact(ctx, new ResourceQueryTemplate(crudModel, ResourceQueryKind.List));

        if ((caps & CapSearch) != 0)
            EmitArtifact(ctx, new ResourceQueryTemplate(crudModel, ResourceQueryKind.Search));
    }

    private static void EmitArtifact(SourceProductionContext ctx, CSharpTemplate template)
    {
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }

    private static void Validate(SourceProductionContext ctx, ImmutableArray<ResourceModel> resources)
    {
        // Per-resource validation
        foreach (var resource in resources)
        {
            // PRAG2602: segment must be kebab-case
            if (!KebabCasePattern.IsMatch(resource.Segment))
            {
                ctx.ReportDiagnostic(
                    ResourceDiagnostics.SegmentNotKebabCase,
                    resource.Location,
                    resource.Segment,
                    resource.TypeName);
            }

            // PRAG2611: [PartOf<TParent>] and [Resource] contradict each other. Reported before the
            // capability advice below, because the answer to "which capabilities should this resource
            // have" is that it should not be a resource.
            if (resource.PartOfParentTypeName is { } parent)
            {
                ctx.ReportDiagnostic(
                    ResourceDiagnostics.ResourceOnAggregatePart,
                    resource.Location,
                    resource.TypeName,
                    parent,
                    resource.Segment);
            }

            // PRAG2612: no capabilities at all, so the pipeline below skips this resource entirely.
            // Reported here rather than where it is skipped, because that skip lives in a Select and
            // a transform cannot report.
            //
            // ⚠️ Unless a trait consumes the segment. [Resource] with no capabilities is the documented
            // way to give the trait endpoints a route prefix — the attribute's own summary says the
            // generator uses it "to resolve route prefixes for traits AND OPTIONALLY auto-scaffold
            // CRUD" — and those endpoints are generated. Reported anyway, the advice ("or remove the
            // attribute") is what PRAG2601 complains about, and following it takes three routes with
            // it: between the two diagnostics there was no way to write such an entity cleanly.
            if (resource.Capabilities == 0
                && resource.PartOfParentTypeName is null
                && !resource.HasRouteConsumingTrait)
            {
                ctx.ReportDiagnostic(
                    ResourceDiagnostics.NoCapabilities,
                    resource.Location,
                    resource.TypeName,
                    resource.Segment);
            }

            // PRAG2605: Capabilities set but no Read
            if (resource.Capabilities != 0 &&
                (resource.Capabilities & 2) == 0) // Read = 2
            {
                ctx.ReportDiagnostic(
                    ResourceDiagnostics.ReadRecommended,
                    resource.Location,
                    resource.TypeName,
                    resource.Segment);
            }
        }

        // PRAG2603: duplicate segments within same boundary
        var byBoundary = resources
            .Where(r => r.BoundaryName is not null)
            .GroupBy(r => r.BoundaryName!);

        foreach (var group in byBoundary)
        {
            var bySegment = group.GroupBy(r => r.Segment);
            foreach (var segGroup in bySegment)
            {
                var items = segGroup.ToList();
                if (items.Count > 1)
                {
                    for (var i = 1; i < items.Count; i++)
                    {
                        ctx.ReportDiagnostic(
                            ResourceDiagnostics.DuplicateSegment,
                            items[i].Location,
                            segGroup.Key,
                            items[0].TypeName,
                            items[i].TypeName,
                            group.Key);
                    }
                }
            }
        }
    }
}
