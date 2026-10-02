using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Manifest.Templates;
using Pragmatic.SourceGenerator.Features.Manifest.Transforms;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Manifest;

/// <summary>
///     Aggregates all compile-time API metadata into a unified manifest JSON.
///     The manifest feeds: OpenAPI enrichment, CLI client generation, WASM client SG.
///     Note: Registration is handled inline in EndpointsFeature for pipeline compatibility.
///     This class provides the public API for manifest generation.
/// </summary>
internal static class ManifestFeature
{
    /// <summary>
    ///     Builds the manifest model from endpoint models, or null when there are no endpoints.
    ///     Compilation is used to discover entity/DTO properties via symbol reflection.
    /// </summary>
    /// <remarks>
    ///     Separated from <see cref="Emit" /> so that the generated file and the entry a host that
    ///     declares its own module hands to the aggregation both come off one pipeline node. Building it
    ///     twice would walk every DTO symbol twice, and the two copies could disagree.
    /// </remarks>
    public static Models.ManifestModel? BuildManifest(
        ImmutableArray<EndpointModel> endpoints,
        Compilation? compilation,
        ImmutableArray<Models.ManifestTypeModel> contributedTypes = default,
        ImmutableArray<Core.DerivedPermissionEntry> derivedPermissions = default)
    {
        if (endpoints.IsEmpty) return null;

        var assemblyName = endpoints[0].Namespace.Contains('.')
            ? endpoints[0].Namespace.Split('.')[0] + "." + endpoints[0].Namespace.Split('.')[1]
            : endpoints[0].Namespace;

        // No local try/catch: this output is registered through RegisterSourceOutputSafe, which already
        // isolates a crash here from the other ~100 outputs and reports it as PRAG9000. Catching first
        // only downgraded the same failure to a warning and hid it from that guard.
        var manifest = ManifestTransform.Build(
            assemblyName, endpoints,
            ImmutableArray<EntityMetadataModel>.Empty, compilation, contributedTypes, derivedPermissions);

        // The generated code may only name what the assembly references. The registry is in
        // Pragmatic.Endpoints, which an assembly with endpoints references; it is still looked up.
        return manifest is null
            ? null
            : manifest with
            {
                RegistersAtLoad = compilation?.GetTypeByMetadataName(
                    "Pragmatic.Endpoints.Manifest.ManifestRegistry") is not null
            };
    }

    /// <summary>Writes the manifest file for a model built by <see cref="BuildManifest" />.</summary>
    public static void Emit(SourceProductionContext ctx, Models.ManifestModel? manifest)
    {
        if (manifest is null) return;

        // PRAG0538: a published type declares a member no response carries. The strip is deliberate —
        // ownership and visibility scopes must not leak through a projection that picked them up — so the
        // fix is the type's, and the silence is what the build ends here.
        foreach (var type in manifest.Types)
        foreach (var member in type.StrippedMembers)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                Endpoints.Diagnostics.EndpointsDiagnostics.MemberIsStrippedFromTheWire,
                Location.None, type.SimpleName, member));
        }

        // SourceOutput.AddSource is the one place that decides what "the template produced nothing"
        // means: an artifact whose content is empty (Validate() returned false) is skipped there.
        ctx.AddSource(new ManifestJsonTemplate(manifest).RenderOutput());
    }
}
