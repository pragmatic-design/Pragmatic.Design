using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Generates advanced persistence code: Inheritance mapping, Hierarchy CTE,
///     Timeline CTE, Polymorphic attachments, Lookup caches, and Loading profiles.
/// </summary>
internal static class AdvancedFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<(ImmutableArray<EntityMetadataModel> Entities, DetectedFeatures Features)> entitiesWithFeatures,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // [Inheritance(Strategy)] → OnModelCreating configuration
        var inheritanceProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                InheritanceMappingTransform.InheritanceAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: InheritanceMappingTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(inheritanceProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateInheritanceMapping(ctx, pair.Left);
        });

        // [GenerateHierarchy] → GetDescendants/GetAncestors CTE
        var hierarchyProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HierarchyQueryTransform.GenerateHierarchyAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: HierarchyQueryTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(hierarchyProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateHierarchyQuery(ctx, pair.Left);
        });

        // [GenerateTimeline] → GetTimeline CTE (via entity pipeline)
        context.RegisterSourceOutputSafe(entitiesWithFeatures, (ctx, pair) =>
        {
            if (!pair.Features.HasPersistenceEFCore)
                return;
            GenerateTimelineQueries(ctx, pair.Entities);
        });

        // [PolymorphicAttachment] → OwnerType/OwnerId + ForOwner<T>() extensions
        var polyAttachProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PolymorphicAttachmentTransform.PolymorphicAttachmentAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: PolymorphicAttachmentTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(polyAttachProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GeneratePolymorphicAttachment(ctx, pair.Left);
        });

        // [LoadWith<T>] → ApplyIncludes() extension
        var loadingProfileProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LoadingProfileTransform.LoadWithAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: LoadingProfileTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(loadingProfileProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateLoadingProfile(ctx, pair.Left);
        });

        // Lookup / Dynamic Enums — cross-reference [Lookup] entities and their consumers
        context.RegisterSourceOutputSafe(entitiesWithFeatures, (ctx, pair) =>
        {
            if (!pair.Features.HasPersistenceEFCore)
                return;
            GenerateLookupNavigations(ctx, pair.Entities);
        });
    }

    // =========================================================================
    // Generate methods
    // =========================================================================

    private static void GenerateInheritanceMapping(
        SourceProductionContext context,
        InheritanceMappingModel model)
    {
        var template = new InheritanceMappingTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateHierarchyQuery(
        SourceProductionContext context,
        HierarchyModel model)
    {
        foreach (var problem in model.Problems)
        {
            var descriptor = problem.Kind switch
            {
                HierarchyProblemKind.ViaRequired => QueryPipelineDiagnostics.HierarchyViaRequired,
                HierarchyProblemKind.ViaNotFound => QueryPipelineDiagnostics.HierarchyViaNotFound,
                HierarchyProblemKind.EdgeRequired => QueryPipelineDiagnostics.HierarchyEdgeRequired,
                _ => QueryPipelineDiagnostics.HierarchyHasNoParentKey
            };

            // ViaNotFound names the candidates as a third argument; the others carry one detail.
            var arguments = problem.Kind == HierarchyProblemKind.ViaNotFound
                ? new object[] { model.TypeName, problem.Detail, problem.Candidates }
                : new object[] { model.TypeName, problem.Detail };

            context.ReportDiagnostic(Diagnostic.Create(descriptor, model.Location, arguments));
        }

        if (model.Trees.Length == 0)
            return;

        var template = new HierarchyQueryTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GeneratePolymorphicAttachment(
        SourceProductionContext context,
        PolymorphicAttachmentModel model)
    {
        var template = new PolymorphicAttachmentTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);

        if (model.HasOwners)
        {
            foreach (var owner in model.OwnerTypes)
            {
                var navTemplate = new VirtualNavigationTemplate(owner, model);
                var navArtifact = navTemplate.RenderOutput();
                if (!navArtifact.IsEmpty)
                    context.AddSource(navArtifact);
            }
        }
    }

    private static void GenerateLoadingProfile(
        SourceProductionContext context,
        LoadingProfileModel model)
    {
        var template = new LoadingProfileTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);

        ReportLoadingProfileDiagnostics(context, model);
    }

    private static void GenerateTimelineQueries(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities)
    {
        if (entities.Length == 0)
            return;

        foreach (var entity in entities)
        {
            if (!entity.IsValid || !entity.IsTemporalRelation)
                continue;
            if (entity.IsFromReference)
                continue;
            if (!entity.HasGenerateTimeline)
                continue;

            var template = new TimelineQueryTemplate(entity);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);
        }
    }

    private static void GenerateLookupNavigations(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities)
    {
        if (entities.Length == 0)
            return;

        var lookups = entities.Where(e => e.IsValid && e is { IsLookup: true, IsFromReference: false }).ToImmutableArray();
        if (lookups.Length == 0)
            return;

        var lookupByType = new Dictionary<string, EntityMetadataModel>(StringComparer.Ordinal);
        foreach (var lookup in lookups)
            lookupByType[lookup.FullTypeName] = lookup;

        foreach (var entity in entities)
        {
            if (!entity.IsValid || entity.IsFromReference || entity.IsLookup)
                continue;

            // A consumer is an entity with a declared relation to the lookup, not one with a property
            // that happens to be called {Lookup}Id: the key is generated from that relation, and the
            // reference member is left to the template below.
            foreach (var nav in entity.Navigations)
            {
                if (nav.NavigationType != "ManyToOne" || nav.ForeignKeyProperty is null
                    || !lookupByType.TryGetValue(nav.TargetFullTypeName ?? nav.TargetTypeName, out var lookup))
                    continue;

                var key = entity.GeneratedRelationProperties.AsImmutableArray()
                    .FirstOrDefault(p => p.Kind == RelationPropertyKind.ForeignKey && p.Name == nav.ForeignKeyProperty);
                var keyType = key?.TypeName ?? lookup.IdType;

                var consumer = new LookupConsumerModel
                {
                    Namespace = entity.Namespace,
                    TypeName = entity.TypeName,
                    FullTypeName = entity.FullTypeName,
                    FkPropertyName = nav.ForeignKeyProperty,
                    FkPropertyType = keyType,
                    IsFkNullable = keyType.EndsWith("?", StringComparison.Ordinal),
                    Lookup = new LookupModel
                    {
                        Namespace = lookup.Namespace,
                        TypeName = lookup.TypeName,
                        FullTypeName = lookup.FullTypeName,
                        IdType = lookup.IdType
                    }
                };

                var navTemplate = new LookupNavigationTemplate(consumer);
                var navArtifact = navTemplate.RenderOutput();
                if (!navArtifact.IsEmpty)
                    context.AddSource(navArtifact);
            }
        }

        foreach (var lookup in lookups)
        {
            var loaderTemplate = new LookupCacheLoaderTemplate(lookup);
            var loaderArtifact = loaderTemplate.RenderOutput();
            if (!loaderArtifact.IsEmpty)
                context.AddSource(loaderArtifact);
        }

        var registrationTemplate = new LookupCacheRegistrationTemplate(lookups);
        var regArtifact = registrationTemplate.RenderOutput();
        if (!regArtifact.IsEmpty)
            context.AddSource(regArtifact);
    }

    private static void ReportLoadingProfileDiagnostics(
        SourceProductionContext context,
        LoadingProfileModel model)
    {
        if (model.HasUnmatchedNavigations)
        {
            foreach (var unmatched in model.UnmatchedNavigationNames)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.DtoNavigationWithoutInclude,
                    Location.None,
                    model.TypeName,
                    unmatched,
                    model.EntityTypeName));
            }
        }

        if (model.MaxDepth > 3)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.DeepIncludeWithoutLoadWith,
                Location.None,
                model.TypeName,
                model.MaxDepth,
                model.EntityTypeName));
        }

        if (model is { HasNavigations: true, NavigationPaths.Length: >= 4 })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.DtoWithManyNavigationLevels,
                Location.None,
                model.TypeName,
                model.NavigationPaths.Length,
                model.EntityTypeName));
        }
    }
}
