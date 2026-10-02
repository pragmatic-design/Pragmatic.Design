using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.FastEnum.Templates;
using Pragmatic.SourceGenerator.Features.FastEnum.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.FastEnum;

/// <summary>
///     Registers the [FastEnum] source generation pipeline.
/// </summary>
internal static class FastEnumFeature
{
    private const string FastEnumAttributeFqn = "Pragmatic.FastEnumAttribute";

    private const string MetadataAttributeFqn = "Pragmatic.Composition.Attributes.PragmaticMetadataAttribute";

    private const string SchemaVersion = "1.0";

    /// <returns>
    ///     The converter registration this compilation generates, for a host that declares its
    ///     <c>[FastEnum]</c> enums itself — the metadata attribute below only reaches a host that
    ///     <i>references</i> the declaring assembly.
    /// </returns>
    public static IncrementalValueProvider<EquatableArray<MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                FastEnumAttributeFqn,
                predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax,
                transform: FastEnumTransform.Transform);

        context.RegisterSourceOutputSafe(provider, static (ctx, model) =>
        {
            if (model is null) return;

            var template = new FastEnumTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                ctx.AddSource(artifact);
        });

        // Per-assembly converter registration. Without it the generated converters exist but nothing
        // adds them to the serializer, and every enum falls through to the reflection-based
        // JsonStringEnumConverter the host registers.
        var converterTypes = provider
            .Where(static m => m is { IsValid: true })
            .Select(static (m, _) => ConverterTypeName(m!))
            .Collect();

        // The metadata attribute is the host's discovery channel, but it only compiles where
        // Pragmatic.Composition is referenced — a plain library with a [FastEnum] enum has no such
        // reference, and emitting it there would break the build with CS0246.
        var assemblyInfo = context.CompilationProvider.Select(static (c, _) => (
            RootNamespace: c.AssemblyName ?? "Global",
            HasCompositionMetadata: c.GetTypeByMetadataName(MetadataAttributeFqn) is not null));

        var registrationInput = converterTypes.Combine(assemblyInfo);

        context.RegisterSourceOutputSafe(registrationInput, static (ctx, tuple) =>
        {
            var (types, assembly) = tuple;
            if (types.IsDefaultOrEmpty)
                return;

            // Ordinal sort so the emitted output does not depend on syntax-tree visit order.
            var ordered = types.Distinct().OrderBy(static t => t, StringComparer.Ordinal).ToList();

            Emit(ctx, new FastEnumConverterRegistrationTemplate(assembly.RootNamespace, ordered).RenderOutput());

            if (assembly.HasCompositionMetadata)
                Emit(ctx, new FastEnumConverterMetadataTemplate(assembly.RootNamespace).RenderOutput());
        });

        // Same condition as the emission above, deliberately adjacent to it: the host is told to call
        // the registration exactly when this compilation generates one.
        return registrationInput.Select(static (tuple, _) =>
        {
            var (types, assembly) = tuple;
            if (types.IsDefaultOrEmpty || !assembly.HasCompositionMetadata)
                return EquatableArray<MetadataEntry>.Empty;

            return ImmutableArray.Create(
                HostLocalRegistration.Create(
                    MetadataCategoryIds.FastEnumConverters,
                    SchemaVersion,
                    GeneratedRegistrationNames.FastEnumConvertersFqn(assembly.RootNamespace)));
        });
    }

    private static string ConverterTypeName(Models.FastEnumModel model)
    {
        var converter = $"{model.TypeName}JsonConverter";
        return string.IsNullOrEmpty(model.Namespace) || model.Namespace == "<global namespace>"
            ? converter
            : $"{model.Namespace}.{converter}";
    }

    private static void Emit(SourceProductionContext ctx, Artifact artifact)
    {
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }
}
