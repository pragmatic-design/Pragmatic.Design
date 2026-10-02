using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Generates projection-related code: Projectable, ComputedFilter,
///     DTO-aware Include extensions, and DTO query extensions.
/// </summary>
internal static class ProjectionFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<EntityMetadataModel>> allEntitiesProvider,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // [MapFrom<T>]-decorated DTOs for include detection
        var dtoMetadataProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MapFrom,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: DtoIncludeTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        // Combine DTOs with entities to produce per-entity include models
        var dtoIncludeProvider = allEntitiesProvider
            .Combine(dtoMetadataProvider)
            .Select(static (combined, _) =>
                DtoIncludeTransform.BuildIncludeModels(combined.Left, combined.Right));

        context.RegisterSourceOutputSafe(dtoIncludeProvider.Combine(features), (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateEntityIncludes(ctx, pair.Left);
        });

        // DTO query extension models
        var dtoQueryProvider = allEntitiesProvider
            .Combine(dtoMetadataProvider)
            .Combine(dtoIncludeProvider)
            .Select(static (combined, _) =>
            {
                var ((entities, dtos), includeModels) = combined;
                return DtoIncludeTransform.BuildQueryModels(entities, dtos, includeModels);
            });

        context.RegisterSourceOutputSafe(dtoQueryProvider.Combine(features), (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateDtoQueryExtensions(ctx, pair.Left);
        });

        // [Projectable] property attribute generator
        var projectableProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.ProjectableAttribute",
                predicate: static (node, _) => node is PropertyDeclarationSyntax { ExpressionBody: not null },
                transform: ProjectableTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect()
            .Combine(features);

        context.RegisterSourceOutputSafe(projectableProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateProjectableCode(ctx, pair.Left);
        });

        // [ComputedFilter] property attribute generator
        var computedFilterProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.ComputedFilterAttribute",
                // Every method, not only an expression-bodied one: a block body is reported (PRAG0733)
                // rather than filtered out here, where nothing could say so.
                predicate: static (node, _) => node is PropertyDeclarationSyntax { ExpressionBody: not null }
                    or MethodDeclarationSyntax,
                transform: ComputedFilterTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect()
            .Combine(features);

        context.RegisterSourceOutputSafe(computedFilterProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateComputedFilterCode(ctx, pair.Left);
        });
    }

    // =========================================================================
    // Generate methods
    // =========================================================================

    private static void GenerateEntityIncludes(
        SourceProductionContext context,
        ImmutableArray<DtoIncludeModel> models)
    {
        foreach (var model in models)
        {
            var template = new EntityIncludeTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);
        }
    }

    private static void GenerateDtoQueryExtensions(
        SourceProductionContext context,
        ImmutableArray<DtoQueryModel> models)
    {
        foreach (var model in models)
        {
            var template = new DtoQueryExtensionsTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);
        }
    }

    private static void GenerateProjectableCode(
        SourceProductionContext context,
        ImmutableArray<ProjectablePropertyModel> properties)
    {
        if (properties.Length == 0)
            return;

        foreach (var property in properties)
            ReportSpecificationsReadingTheRow(
                context, property.EntityTypeName, property.PropertyName, property.SpecificationsReadingTheRow);

        var groups = properties.GroupBy(p => p.EntityFullTypeName);
        foreach (var group in groups)
        {
            var first = group.First();
            var model = new ProjectableModel
            {
                Namespace = first.EntityNamespace,
                TypeName = first.EntityTypeName,
                Accessibility = first.EntityAccessibility,
                Properties = group.ToImmutableArray()
            };

            var template = new ProjectableTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);
        }
    }

    private static void GenerateComputedFilterCode(
        SourceProductionContext context,
        ImmutableArray<ComputedFilterTransform.TransformResult> results)
    {
        if (results.Length == 0)
            return;

        foreach (var rejected in results.Where(r => r.Rejection is not null))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ComputedFilterDiagnostics.ComputedFilterCannotBeGenerated,
                rejected.Location?.ToLocation(),
                rejected.EntityTypeName,
                rejected.PropertyName,
                rejected.Rejection));
        }

        foreach (var result in results)
            ReportSpecificationsReadingTheRow(
                context, result.EntityTypeName, result.PropertyName, result.SpecificationsReadingTheRow);

        var groups = results
            .Where(r => r.Rejection is null)
            .GroupBy(r =>
                string.IsNullOrEmpty(r.EntityNamespace) ? r.EntityTypeName : $"{r.EntityNamespace}.{r.EntityTypeName}");

        foreach (var group in groups)
        {
            var first = group.First();
            var model = new ComputedFilterModel
            {
                Namespace = first.EntityNamespace,
                TypeName = first.EntityTypeName,
                Accessibility = first.EntityAccessibility,
                Properties = group.Select(r => new ComputedFilterPropertyModel
                {
                    PropertyName = r.PropertyName,
                    ExpressionBody = r.ExpressionBody,
                    Parameters = r.Parameters,
                    Arguments = r.Arguments
                }).ToImmutableArray()
            };

            var template = new ComputedFilterTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                context.AddSource(artifact);
        }
    }

    private static void ReportSpecificationsReadingTheRow(
        SourceProductionContext context, string entity, string member,
        EquatableArray<SpecificationReadingTheRowModel> specifications)
    {
        foreach (var specification in specifications)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ComputedBodyDiagnostics.SpecificationReadsTheRow,
                specification.Location?.ToLocation(),
                entity,
                member,
                specification.Argument));
        }
    }
}
