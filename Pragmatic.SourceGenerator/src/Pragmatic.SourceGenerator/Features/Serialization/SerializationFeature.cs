using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Serialization.Models;
using Pragmatic.SourceGenerator.Features.Serialization.Templates;
using Pragmatic.SourceGenerator.Features.Serialization.Transforms;

namespace Pragmatic.SourceGenerator.Features.Serialization;

/// <summary>
///     Emits a per-assembly <c>JsonSerializerContext</c> subclass covering the assembly's
///     serializable boundary types (AOT-safe, no STJ source generator). Opt-in via the build property
///     <c>PragmaticGenerateJsonContext</c> (or <c>PublishAot</c>).
/// </summary>
internal static class SerializationFeature
{
    private const string SchemaVersion = "1.0";

    /// <returns>
    ///     The JSON context registration this compilation generates, for a host whose serializable
    ///     types are declared in the host project itself; and whether it emits the context at all, for
    ///     the features that hand it to a runtime component (the redaction map).
    /// </returns>
    /// <param name="context">The generator initialization context.</param>
    /// <param name="features">The features detected on the referenced assemblies.</param>
    /// <param name="endpoints">
    ///     Endpoint models, for the request bodies this generator itself emits. Optional so the feature
    ///     stays testable on its own; without them the context covers everything except request bodies,
    ///     which is what it did before they were threaded in.
    /// </param>
    /// <param name="redactedTypes">
    ///     The types the redaction map carries. The redactor serializes them inside the logger, where a
    ///     reflection failure under Native AOT loses the entry.
    /// </param>
    public static (IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Registrations,
        IncrementalValueProvider<bool> EmitsContext) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<Endpoints.Models.EndpointModel>>? endpoints = null,
        IncrementalValueProvider<ImmutableArray<JsonRootContribution>>? redactedTypes = null)
    {
        // Opt-in: build property (<PragmaticGenerateJsonContext>/<PublishAot>) OR the assembly marker
        // attribute. The attribute makes the feature testable and works in project-ref scenarios where
        // the package's CompilerVisibleProperty .props is not imported.
        var optInFromProperty = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            bool Flag(string key) =>
                provider.GlobalOptions.TryGetValue($"build_property.{key}", out var value)
                && value.Equals("true", StringComparison.OrdinalIgnoreCase);
            return Flag("PragmaticGenerateJsonContext") || Flag("PublishAot");
        });

        var optInFromAttribute = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.Assembly.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Pragmatic.Serialization.PragmaticGenerateJsonContextAttribute"));

        var optIn = optInFromProperty.Combine(optInFromAttribute).Select(static (p, _) => p.Left || p.Right);

        var messages = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.MessageHandler,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: SerializationTransform.FromMessageHandler)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        var jobs = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.Job,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: SerializationTransform.FromJob)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        var recurring = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.RecurringJob,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: SerializationTransform.FromJob)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        var events = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.EventHandler,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: SerializationTransform.FromEventHandler)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        var sagas = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.Saga,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: SerializationTransform.FromSaga)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        // HTTP boundary: mapping DTOs are the declared request/response shapes. Raw entity returns and
        // projections/patch DTOs are a later phase (they depend on Actions/boundary return-type resolution).
        var mapFrom = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.MapFrom,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: SerializationTransform.FromMappingDto)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        var mapTo = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.MapTo,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: SerializationTransform.FromMappingDto)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        // SSE streaming endpoints serialize their item type per event through the host options seam.
        var streamingItems = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeNames.Endpoint,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: SerializationTransform.FromStreamingEndpoint)
            .Where(static m => m is not null).Select(static (m, _) => m!);

        // Generated request bodies. They have no symbol to walk — this generator is what creates them —
        // so an endpoint read from source records its shape in the transform, while the action symbol is
        // still in hand. Endpoints the generator assembles itself (Resource CRUD, traits) carry no symbol
        // at any point and are resolved here from the type text they were written with.
        // Left out, every POST body fell back to reflection and a real AOT publish threw at the first call.
        var bodies = endpoints?.Combine(context.CompilationProvider).Select(static (pair, _) =>
            {
                var (models, compilation) = pair;
                var resolver = new Analysis.JsonTypeExpressionResolver(compilation);
                var builder = ImmutableArray.CreateBuilder<JsonRootContribution>();

                foreach (var model in models)
                {
                    var contribution = model.JsonContribution
                                       ?? Analysis.JsonBodyDtoShape.For(model, resolver);
                    if (contribution is not null)
                        builder.Add(contribution);
                }

                return builder.ToImmutable();
            })
            ?? context.CompilationProvider.Select(static (_, _) => ImmutableArray<JsonRootContribution>.Empty);

        var contributions = messages.Collect()
            .Combine(jobs.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(recurring.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(events.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(sagas.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(mapFrom.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(mapTo.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(streamingItems.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(bodies).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(redactedTypes
                     ?? context.CompilationProvider.Select(static (_, _) => ImmutableArray<JsonRootContribution>.Empty))
            .Select(static (p, _) => p.Left.AddRange(p.Right));

        var rootNs = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Global");

        var pipeline = contributions.Combine(rootNs).Combine(features).Combine(optIn);

        context.RegisterSourceOutputSafe(pipeline, static (ctx, tuple) =>
        {
            var (((contribs, ns), feats), optedIn) = tuple;
            if (!Generates(contribs, ns, feats, optedIn))
                return;

            var model = BuildModel(contribs, ns);

            Emit(ctx, new PragmaticJsonContextTemplate(
                model,
                Core.VirtualFolderHints.ForAssembly("Json", "Context")).RenderOutput());
            Emit(ctx, new JsonContextRegistrationTemplate(ns).RenderOutput());
            Emit(ctx, new JsonContextMetadataTemplate(ns).RenderOutput());
        });

        // The host is told to call the registration under the same condition that emits it.
        var registrations = pipeline.Select(static (tuple, _) =>
        {
            var (((contribs, ns), feats), optedIn) = tuple;
            if (!Generates(contribs, ns, feats, optedIn))
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.Create(
                    Composition.MetadataCategoryIds.JsonContexts,
                    SchemaVersion,
                    GeneratedRegistrationNames.JsonContextFqn(ns)));
        });

        // Under the same condition again, so a feature that names the context names one that exists.
        var emitsContext = pipeline.Select(static (tuple, _) =>
        {
            var (((contribs, ns), feats), optedIn) = tuple;
            return Generates(contribs, ns, feats, optedIn);
        });

        return (registrations, emitsContext);
    }

    /// <summary>Whether this compilation emits a generated JSON context (and therefore a registration).</summary>
    private static bool Generates(
        ImmutableArray<JsonRootContribution> contributions,
        string rootNamespace,
        DetectedFeatures features,
        bool optedIn)
        => optedIn
           && features.HasSerialization
           && !contributions.IsDefaultOrEmpty
           && BuildModel(contributions, rootNamespace).Objects.Count > 0;

    private static JsonContextModel BuildModel(ImmutableArray<JsonRootContribution> contribs, string ns)
    {
        var objects = new Dictionary<string, JsonObjectModel>(StringComparer.Ordinal);
        var leaves = new Dictionary<string, JsonLeafModel>(StringComparer.Ordinal);
        var collections = new Dictionary<string, JsonCollectionModel>(StringComparer.Ordinal);

        foreach (var c in contribs)
        {
            foreach (var obj in c.Objects)
                objects[obj.TypeExpr] = obj;
            foreach (var leaf in c.Leaves)
                leaves[leaf.TypeExpr] = leaf;
            foreach (var coll in c.Collections)
                collections[coll.TypeExpr] = coll;
        }

        return new JsonContextModel(
            Namespace: ns,
            Objects: objects.Values.OrderBy(o => o.TypeExpr, StringComparer.Ordinal).ToImmutableArray(),
            Leaves: leaves.Values.OrderBy(l => l.TypeExpr, StringComparer.Ordinal).ToImmutableArray(),
            Collections: collections.Values.OrderBy(c => c.TypeExpr, StringComparer.Ordinal).ToImmutableArray());
    }

    private static void Emit(SourceProductionContext ctx, Artifact artifact)
    {
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }
}
