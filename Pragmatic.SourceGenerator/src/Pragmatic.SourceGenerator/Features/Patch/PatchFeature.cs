using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Patch.Diagnostics;
using Pragmatic.SourceGenerator.Features.Patch.Models;
using Pragmatic.SourceGenerator.Features.Patch.Templates;
using Pragmatic.SourceGenerator.Features.Patch.Transforms;

namespace Pragmatic.SourceGenerator.Features.Patch;

/// <summary>
///     Patch feature: registers pipeline for [GeneratePatch&lt;TEntity&gt;] generation.
///     Activated when Pragmatic.Patch runtime is referenced.
/// </summary>
internal static class PatchFeature
{
    /// <returns>
    ///     Descriptions of the generated patch DTOs, for the API manifest. The user declares only an empty
    ///     partial record, so the manifest cannot discover the properties by resolving the symbol — the shape
    ///     has to be contributed. See <see cref="PatchManifestTypeBuilder" />.
    /// </returns>
    public static IncrementalValueProvider<ImmutableArray<Manifest.Models.ManifestTypeModel>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var patchProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.GeneratePatch,
                GeneratorHelpers.IsClassOrRecord,
                PatchTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Report diagnostics (always, regardless of HasPatch — the attribute IS from Patch package)
        context.RegisterSourceOutputSafe(patchProvider, ReportDiagnostics);

        // Generate only for valid models
        var validPatches = patchProvider.Where(static m => m.IsValid);
        context.RegisterSourceOutputSafe(validPatches, GeneratePatchType);
        context.RegisterSourceOutputSafe(validPatches, GeneratePatchConverter);

        return validPatches.Collect().Select(static (models, _) => PatchManifestTypeBuilder.Build(models));
    }

    private static void ReportDiagnostics(SourceProductionContext context, PatchModel model)
    {
        // Before the validity gate: a [PatchIgnore] naming nothing is worth saying on a patch that is
        // otherwise perfectly good — that is exactly when it is dangerous, because the property the
        // author meant to protect is still in it.
        foreach (var name in model.UnmatchedIgnores)
            context.ReportDiagnostic(Diagnostic.Create(
                PatchDiagnostics.PatchIgnoreNameNotFound,
                Location.None,
                model.TypeName, name, model.EntityName));

        if (model.IsValid)
            return;

        switch (model.Reason)
        {
            // NotPartial: PRAG2200 is the companion analyzer's.

            case InvalidReason.EntityTypeNotFound:
                context.ReportDiagnostic(Diagnostic.Create(
                    PatchDiagnostics.EntityTypeNotFound, Location.None, model.TypeName));
                break;

            case InvalidReason.NoProperties:
                context.ReportDiagnostic(Diagnostic.Create(
                    PatchDiagnostics.NoProperties, Location.None, model.EntityName, model.TypeName));
                break;
        }
    }

    private static void GeneratePatchType(SourceProductionContext context, PatchModel model)
    {
        var artifact = new PatchTypeTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GeneratePatchConverter(SourceProductionContext context, PatchModel model)
    {
        var artifact = new PatchConverterTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }
}
