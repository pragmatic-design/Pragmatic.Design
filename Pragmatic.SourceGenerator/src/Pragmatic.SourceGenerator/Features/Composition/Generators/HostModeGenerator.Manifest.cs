using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Generators;

/// <summary>
///     Aggregated manifest generation from referenced module metadata.
/// </summary>
internal static partial class HostModeGenerator
{
    /// <summary>
    ///     Merges every module manifest the host can see into one document, plus the compile-time
    ///     OpenAPI spec built from the same list.
    /// </summary>
    /// <remarks>
    ///     <paramref name="localMetadata"/> is the host's own, for modules it declares in its own
    ///     project. It carries the same manifest document its <c>_Metadata.PragmaticManifest.g.cs</c>
    ///     writes, but no <c>[assembly: PragmaticMetadata]</c> can be read back for it — this run is what
    ///     emits that attribute — so it arrives directly. Without it a host that declares its own module
    ///     aggregated nothing, and neither this file nor the OpenAPI document was emitted at all.
    /// </remarks>
    private static void GenerateAggregatedManifest(
        SourceProductionContext context,
        ImmutableArray<AssemblyMetadataModel> assemblyMetadata,
        ImmutableArray<AssemblyMetadataModel> localMetadata,
        Compilation compilation)
    {
        // Collect all manifest JSON entries from referenced assemblies, then from this one
        var manifestJsons = new List<string>();
        foreach (var assembly in assemblyMetadata.AddRange(localMetadata))
        {
            foreach (var entry in assembly.Entries)
            {
                if (entry.Category != MetadataCategoryIds.Manifest) continue;
                if (string.IsNullOrEmpty(entry.JsonData)) continue;

                // These strings come from [PragmaticMetadata] on REFERENCED assemblies — produced by
                // another compilation, possibly by an older generator. They are embedded verbatim
                // below, and MetadataJsonBuilder rejects anything that is not valid JSON. An
                // unguarded rejection would abort the whole GenerateHost: this runs before
                // Host.Services.g.cs, Pragmatic.g.cs, the invoke dispatcher and the topology report,
                // so one malformed module manifest would take down the entire host composition to
                // protect a purely informational artifact. Drop the offending module instead.
                if (!JsonValidator.IsValid(entry.JsonData))
                {
                    context.ReportDiagnostic(
                        CompositionDiagnostics.ModuleManifestUnreadable,
                        Location.None,
                        assembly.AssemblyName);
                    continue;
                }

                manifestJsons.Add(entry.JsonData!);
            }
        }

        var assemblyName = compilation.AssemblyName ?? "Host";

        // With the registry referenced the host registers PragmaticManifest (HasAggregatedManifest probes
        // the same type), so the class has to exist even when no module published anything yet — the
        // skeleton pragmatic-new-app builds first. The OpenAPI document below gets the same answer; without it
        // the host fails with CS0103.
        var hasManifestRegistry = compilation.GetTypeByMetadataName(
            "Pragmatic.Endpoints.Manifest.ManifestRegistry") is not null;

        if (manifestJsons.Count > 0 || hasManifestRegistry)
            GenerateManifestDocument(context, compilation, assemblyName, manifestJsons);

        GenerateOpenApiDocument(context, compilation, assemblyName, manifestJsons);
    }

    private static void GenerateManifestDocument(
        SourceProductionContext context,
        Compilation compilation,
        string assemblyName,
        List<string> manifestJsons)
    {
        // Build aggregated manifest JSON by merging all module manifests
        var b = new Pragmatic.SourceGen.MetadataJsonBuilder(indent: true);
        b.StartObject();
        b.Property("$schema").Value("pragmatic-manifest/v1");
        b.Property("version").Value("1.0.0");
        b.Property("assembly").Value(assemblyName);
        b.Property("aggregated").Value(true);
        b.Property("moduleCount").Value(manifestJsons.Count);

        // Embed each module manifest as raw JSON in the "modules" array. Validated above.
        b.Property("modules").StartArray();
        foreach (var json in manifestJsons)
            b.RawValue(json); // Each module manifest as an inline JSON object
        b.EndArray();

        b.EndObject();
        var aggregatedJson = b.ToString();

        // Check if ManifestRegistry is available (host references Pragmatic.Endpoints):
        // without this registration ManifestReader.ReadAll() is empty and runtime OpenAPI
        // enrichment (error responses, permissions, deprecation) silently does nothing.
        var hasManifestRegistry = compilation.GetTypeByMetadataName(
            "Pragmatic.Endpoints.Manifest.ManifestRegistry") is not null;

        var manifest = new Manifest.Templates.AggregatedManifestTemplate(
            assemblyName,
            aggregatedJson,
            manifestJsons.Count,
            hasManifestRegistry).RenderOutput();

        if (!manifest.IsEmpty)
            context.AddSource(manifest);
    }

    /// <summary>
    ///     The compile-time OpenAPI 3.1 document, derived from the same manifests.
    /// </summary>
    /// <remarks>
    ///     A host that references <c>Pragmatic.Endpoints.OpenApi</c> can serve the document, so it always
    ///     gets one — with no operations before its first endpoint. Emitting nothing sent the mount to
    ///     the 404 written for a generator that failed. A host that cannot serve it keeps
    ///     emitting nothing when there is nothing to describe.
    /// </remarks>
    private static void GenerateOpenApiDocument(
        SourceProductionContext context,
        Compilation compilation,
        string assemblyName,
        List<string> manifestJsons)
    {
        var hasOpenApiRegistry = compilation.GetTypeByMetadataName(
            "Pragmatic.Endpoints.OpenApi.PragmaticOpenApiRegistry") is not null;

        var openApi = Manifest.Templates.OpenApiJsonGenerator.Generate(assemblyName, manifestJsons);
        var json = openApi.Json
                   ?? (hasOpenApiRegistry ? Manifest.Templates.OpenApiJsonGenerator.WithNoOperations(assemblyName) : null);
        if (json is null)
            return;

        var document = new Manifest.Templates.OpenApiDocumentTemplate(
            assemblyName,
            json,
            openApi.RequiresAuthentication,
            hasOpenApiRegistry).RenderOutput();

        if (!document.IsEmpty)
            context.AddSource(document);
    }
}
