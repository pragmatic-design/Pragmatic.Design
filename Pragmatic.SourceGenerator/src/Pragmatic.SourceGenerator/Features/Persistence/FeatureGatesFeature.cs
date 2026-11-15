using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Says that this assembly has an operation refused when a feature flag is off, so a host can answer for
///     the <c>IFeatureFlags</c> the generated invoker evaluates — or say it cannot (<c>PRAG1616</c>).
/// </summary>
/// <remarks>
///     The attribute is in Abstractions, which every module has; the evaluation is in Pragmatic.FeatureFlags,
///     which a host has to reference. Without it the gate is shut for everyone.
/// </remarks>
internal static class FeatureGatesFeature
{
    private const string SchemaVersion = "1.0";

    /// <returns>The host-local entry, for a host that declares a gated operation itself.</returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var named = Declarations(context, AttributeNames.RequiresFeature);
        var typed = Declarations(context, AttributeNames.RequiresFeature + "`1");

        var declarations = named.Combine(typed)
            .Combine(features)
            .Select(static (pair, _) => (Count: pair.Left.Left + pair.Left.Right, pair.Right.HasComposition));

        context.RegisterSourceOutputSafe(declarations, static (ctx, data) =>
        {
            if (data.Count == 0 || !data.HasComposition)
                return;

            ctx.AddSource(new FeatureGatesMetadataTemplate(data.Count).RenderOutput());
        });

        return declarations.Select(static (data, _) => data.Count == 0 || !data.HasComposition
            ? EquatableArray<Composition.Models.MetadataEntry>.Empty
            : new EquatableArray<Composition.Models.MetadataEntry>(ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.CreatePayload(
                    Composition.MetadataCategoryIds.FeatureGates,
                    SchemaVersion,
                    FeatureGatesMetadataTemplate.Payload(data.Count)))));
    }

    private static IncrementalValueProvider<int> Declarations(
        IncrementalGeneratorInitializationContext context, string attributeMetadataName)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ctx.TargetSymbol.Name)
            .Collect()
            .Select(static (names, _) => names.Length);
}
