using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Diagnostics;
using Pragmatic.SourceGenerator.Features.Configuration.Models;
using Pragmatic.SourceGenerator.Features.Configuration.Templates;

namespace Pragmatic.SourceGenerator.Features.Configuration;

internal static class ConfigurationFeature
{
    /// <returns>
    ///     The catalogue registration for sections declared in <b>this</b> compilation. A host that
    ///     declares its own <c>[Configuration]</c> writes the metadata attribute in the same run that
    ///     would have to read it, so the entry is handed over directly — see <c>HostLocalRegistration</c>.
    ///     Without it the host's own sections are absent from the catalogue and
    ///     <c>ConfigurationPreflight</c> passes a configuration missing exactly those keys.
    /// </returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // [Configuration] per-type pipeline
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Configuration,
                GeneratorHelpers.IsClass,
                ConfigurationTransform.Transform)
            .Where(m => m is not null);

        context.RegisterSourceOutputSafe(
            provider.Combine(features).Where(x => x.Right.HasConfiguration),
            static (ctx, x) => GenerateRegistration(ctx, x.Left!));

        // Per-assembly aggregator pipeline
        var allConfigs = provider.Select((m, _) => m!)
            .WithTrackingName(TrackingNames.ConfigurationConfigurations)
            .Collect();

        var aggregatorInput = allConfigs.Combine(features);

        context.RegisterSourceOutputSafe(aggregatorInput, static (ctx, x) =>
        {
            var (models, detected) = x;
            if (!detected.HasConfiguration)
                return;
            GenerateAggregator(ctx, models);
        });

        // Metadata pipeline (only when Composition is referenced)
        var compilationInfo = context.CompilationProvider
            .Select(static (c, _) => (
                IsDebug: c.Options.OptimizationLevel == OptimizationLevel.Debug,
                HasComposition: CompositionDetector.IsCompositionReferenced(c)));

        var metadataInput = allConfigs.Combine(compilationInfo);

        context.RegisterSourceOutputSafe(metadataInput, GenerateConfigurationMetadata);

        // Same condition as the metadata above: emitted only where a catalogue registration is actually
        // generated, so the host never calls a method that does not exist.
        return metadataInput.Select(static (input, _) =>
        {
            var (models, info) = input;
            if (!info.HasComposition || models.IsDefaultOrEmpty)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            var validModels = models.Where(m => m.IsPartial && !m.IsStaticOrAbstract).ToImmutableArray();
            if (validModels.IsDefaultOrEmpty)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return new EquatableArray<Composition.Models.MetadataEntry>(ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.Create(
                    Composition.MetadataCategoryIds.Configuration,
                    MetadataSchemaVersions.Configuration,
                    ConfigurationCatalogTemplate.FqnFor(CatalogNamespace(validModels)))));
        });
    }

    private static void GenerateRegistration(SourceProductionContext ctx, ConfigurationModel model)
    {
        // Not partial: nothing to generate into. PRAG2000 is the companion analyzer's.
        if (!model.IsPartial)
            return;

        if (model.IsStaticOrAbstract)
        {
            ctx.ReportDiagnostic(ConfigurationDiagnostics.MustNotBeStaticOrAbstract, (Location?)null, model.TypeName);
            return;
        }

        // PRAG2050: a [Required] property that also carries a default value — the default satisfies
        // the requirement, so [Required] is likely redundant or the default is unintended.
        foreach (var prop in model.Properties)
            if (prop is { IsRequired: true, HasDefaultValue: true })
                ctx.ReportDiagnostic(ConfigurationDiagnostics.RequiredWithDefault, (Location?)null, prop.Name, model.TypeName);

        // A misshapen [ConfigInvariant] is reported, not dropped: dropped, the rule would compile and never run.
        foreach (var invariant in model.MisshapenInvariants)
            ctx.ReportDiagnostic(ConfigurationDiagnostics.InvariantMustBeCallable,
                invariant.Location?.ToLocation(), invariant.MethodName, model.TypeName);

        // [ConfigInvariant] methods → generate an IValidateOptions<T> and wire its registration.
        string? validatorType = null;
        if (model.HasInvariants)
        {
            var validatorTemplate = new OptionsValidatorTemplate(model);
            var validatorArtifact = validatorTemplate.RenderOutput();
            ctx.AddSource(validatorArtifact);
            validatorType = validatorTemplate.FullyQualifiedName;
        }

        var artifact = new RegistrationTemplate(model, validatorType).RenderOutput();
        ctx.AddSource(artifact);
    }

    private static void GenerateConfigurationMetadata(
        SourceProductionContext ctx,
        (ImmutableArray<ConfigurationModel> Models, (bool IsDebug, bool HasComposition) Info) input)
    {
        var (models, (isDebug, hasComposition)) = input;

        if (!hasComposition)
            return;

        if (models.IsDefaultOrEmpty)
            return;

        // Same filter the aggregator applies: a static/abstract type is rejected by PRAG2001 and gets no
        // registration, so naming it here would point the host at a method that does not exist.
        var validModels = models.Where(m => m.IsPartial && !m.IsStaticOrAbstract).ToImmutableArray();

        string? registrationFqn = null;
        if (!validModels.IsDefaultOrEmpty)
        {
            var catalogNamespace = CatalogNamespace(validModels);
            ctx.AddSource(new ConfigurationCatalogTemplate(validModels, catalogNamespace).RenderOutput());
            registrationFqn = ConfigurationCatalogTemplate.FqnFor(catalogNamespace);
        }

        var artifact = new ConfigurationMetadataTemplate(models, isDebug, registrationFqn).RenderOutput();
        ctx.AddSource(artifact);
    }

    /// <summary>
    ///     Where the catalogue contribution is emitted: the shortest namespace shared by the declared
    ///     sections, so the generated class sits with the module rather than in a framework namespace.
    /// </summary>
    private static string CatalogNamespace(ImmutableArray<ConfigurationModel> models)
    {
        var namespaces = models
            .Select(m => m.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToList();

        return namespaces.Count == 0 ? "" : NamespacePrefixHelper.DerivePrefix(namespaces);
    }

    private static void GenerateAggregator(
        SourceProductionContext ctx,
        ImmutableArray<ConfigurationModel> models)
    {
        if (models.IsDefaultOrEmpty)
            return;

        // Only include valid models. Static/abstract types are rejected (PRAG2001) and never get a
        // RegistrationTemplate, so excluding them here too avoids the aggregator emitting a call to a
        // method that was never generated (CS0103) on top of the diagnostic.
        var validModels = models.Where(m => m.IsPartial && !m.IsStaticOrAbstract).ToImmutableArray();

        if (validModels.IsDefaultOrEmpty)
            return;

        // Emit the [Sensitive] key classifier (and wire its registration into the aggregator) only when at
        // least one sensitive property exists; otherwise the NullSensitiveKeyClassifier default stands.
        string? classifierType = null;
        if (SensitiveKeyClassifierTemplate.SensitiveKeys(validModels).Length > 0)
        {
            var classifierTemplate = new SensitiveKeyClassifierTemplate(validModels);
            var classifierArtifact = classifierTemplate.RenderOutput();
            ctx.AddSource(classifierArtifact);
            classifierType = classifierTemplate.FullyQualifiedName;
        }

        var artifact = new AggregatorTemplate(validModels, classifierType).RenderOutput();
        ctx.AddSource(artifact);
    }
}
