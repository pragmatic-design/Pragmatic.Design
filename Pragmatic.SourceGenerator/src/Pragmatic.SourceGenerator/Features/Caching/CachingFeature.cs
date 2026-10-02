using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Caching.Diagnostics;
using Pragmatic.SourceGenerator.Features.Caching.Models;
using Pragmatic.SourceGenerator.Features.Caching.Templates;
using Pragmatic.SourceGenerator.Features.Caching.Transforms;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Caching;

internal static class CachingFeature
{
    /// <summary>Compiled once — reused across all SG invocations and templates.</summary>
    internal static readonly Regex DurationRegex =
        new(@"^(\d+)([smhd])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagPlaceholderRegex =
        new(@"\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>
    ///     Registers the caching pipelines and returns the domain-event handlers this feature will
    ///     generate, for the Composition feature to register on the dispatcher.
    /// </summary>
    /// <remarks>
    ///     The handler types do not exist in the compilation being analysed — this generator is what
    ///     creates them — so Composition cannot discover them by scanning for <c>[EventHandler]</c>.
    ///     They have to be contributed from the same model the template renders, or the generated
    ///     handler would be a class nobody ever resolves.
    /// </remarks>
    public static (IncrementalValueProvider<ImmutableArray<EventHandlerModel>> Handlers,
        IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> LocalRegistrations) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // [Cacheable] pipeline
        var cacheableProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Cacheable,
                GeneratorHelpers.IsClassOrRecord,
                CachingTransform.TransformCacheable)
            .Where(m => m is not null);

        context.RegisterSourceOutputSafe(
            cacheableProvider.Combine(features).Where(x => x.Right.HasCaching),
            static (ctx, x) => GenerateCacheable(ctx, x.Left!));

        // [InvalidatesCache] pipeline
        var invalidatesProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.InvalidatesCache,
                GeneratorHelpers.IsClassOrRecord,
                CachingTransform.TransformInvalidates)
            .Where(m => m is not null);

        context.RegisterSourceOutputSafe(
            invalidatesProvider.Combine(features).Where(x => x.Right.HasCaching),
            static (ctx, x) => GenerateInvalidator(ctx, x.Left!));

        // Metadata pipeline (only when Composition is referenced)
        var allCacheables = cacheableProvider.Select((m, _) => m!)
            .WithTrackingName(TrackingNames.CachingCacheables)
            .Collect();
        var allInvalidators = invalidatesProvider.Select((m, _) => m!)
            .WithTrackingName(TrackingNames.CachingInvalidators)
            .Collect();

        var compilationInfo = context.CompilationProvider
            .Select(static (c, _) => (
                IsDebug: c.Options.OptimizationLevel == OptimizationLevel.Debug,
                HasComposition: CompositionDetector.IsCompositionReferenced(c)));

        var metadataInput = allCacheables.Combine(allInvalidators).Combine(compilationInfo);

        context.RegisterSourceOutputSafe(metadataInput, GenerateCachingMetadata);

        // Handlers for the events whose invalidation is generated above, described rather than
        // discovered — see the remarks on Register.
        var handlers = invalidatesProvider
            .Combine(features)
            .Where(static x => x.Right.HasCaching && x.Left!.IsDomainEvent)
            .Select(static (x, _) => ToEventHandlerModel(x.Left!))
            .Collect();

        return (handlers, metadataInput.Select(static (x, _) => LocalCachingRegistrations(x)));
    }

    /// <summary>
    ///     The cache-category metadata, handed to the host directly when the declaring assembly *is*
    ///     the host. It cannot travel as <c>[assembly: PragmaticMetadata]</c>: that attribute is
    ///     emitted by this same generator run, and the host reads metadata off its references only.
    /// </summary>
    /// <remarks>
    ///     Carries the payload rather than naming an entry point, because the host renders
    ///     <c>ForCategory&lt;T&gt;</c> inline from the models instead of calling anything. The guard
    ///     mirrors <see cref="GenerateCachingMetadata"/> exactly — the host has to see these entries in
    ///     the shape declaring them in a library produces, and in no other.
    /// </remarks>
    private static EquatableArray<Composition.Models.MetadataEntry> LocalCachingRegistrations(
        ((ImmutableArray<CacheableModel> Cacheables, ImmutableArray<InvalidatesModel> Invalidators),
            (bool IsDebug, bool HasComposition)) input)
    {
        var ((cacheables, invalidators), (isDebug, hasComposition)) = input;

        if (!hasComposition || (cacheables.IsDefaultOrEmpty && invalidators.IsDefaultOrEmpty))
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        return ImmutableArray.Create(
            Composition.Models.HostLocalRegistration.CreatePayload(
                Composition.MetadataCategoryIds.Caching,
                MetadataSchemaVersions.Caching,
                new CachingMetadataTemplate(cacheables, invalidators, isDebug).BuildJson()));
    }

    /// <summary>
    ///     Describes the handler <see cref="CacheInvalidationHandlerTemplate"/> is about to emit, in
    ///     the shape the Composition registration pipeline consumes.
    /// </summary>
    private static EventHandlerModel ToEventHandlerModel(InvalidatesModel model) => new()
    {
        Namespace = model.Namespace,
        TypeName = CacheInvalidationHandlerTemplate.HandlerTypeName(model),
        FullTypeName = CacheInvalidationHandlerTemplate.HandlerFullTypeName(model),
        // One event, and that is this handler's shape: the generator writes it, for exactly the
        // invalidation the entity declared. The list exists because a hand-written handler may take
        // several.
        EventTypeFullNames = new Pragmatic.SourceGen.EquatableArray<string>(
            System.Collections.Immutable.ImmutableArray.Create(model.TypeFqn)),
        LocationInfo = model.Location
    };

    // =========================================================================
    // Generate
    // =========================================================================

    private static void GenerateCacheable(SourceProductionContext ctx, CacheableModel model)
    {
        // Not partial: nothing to generate into. PRAG1700 is the companion analyzer's.
        if (!model.IsPartial)
            return;

        if (!IsValidDuration(model.Duration))
            ctx.ReportDiagnostic(CachingDiagnostics.InvalidDuration, model.Location?.ToLocation(), model.Duration);

        if (model.KeyProperties.IsDefaultOrEmpty && model.TotalPropertyCount == 0)
            ctx.ReportDiagnostic(CachingDiagnostics.NoKeyProperties, model.Location?.ToLocation(), model.TypeName);

        if (model.KeyProperties.IsDefaultOrEmpty && model.TotalPropertyCount > 0)
            ctx.ReportDiagnostic(CachingDiagnostics.AllPropertiesExcluded, model.Location?.ToLocation(), model.TypeName);

        foreach (var property in model.UnreadableKeyProperties)
            ctx.ReportDiagnostic(
                CachingDiagnostics.KeyPropertyNotFullyRead, model.Location?.ToLocation(),
                property, model.TypeName);

        ValidateTagPlaceholders(ctx, model);
        ValidateDuplicateOrders(ctx, model);

        var artifact = new CacheableTemplate(model).RenderOutput();
        ctx.AddSource(artifact);
    }

    private static void GenerateInvalidator(SourceProductionContext ctx, InvalidatesModel model)
    {
        if (model.IsDomainEvent)
        {
            // A domain event is dispatched, not invoked: the bridge is a handler type of its own, so
            // the event itself never has to be partial. PRAG1704 belongs to the mutation path only.
            ctx.AddSource(new CacheInvalidationHandlerTemplate(model).RenderOutput());

            // An event that IS partial keeps its ICacheInvalidator: it is inert on the dispatch path,
            // but code that calls InvalidateAsync by hand today would stop compiling without it.
            if (model.IsPartial)
                ctx.AddSource(new InvalidatorTemplate(model).RenderOutput());

            return;
        }

        if (!model.IsPartial)
        {
            ctx.ReportDiagnostic(CachingDiagnostics.InvalidatesMustBePartial, model.Location?.ToLocation(), model.TypeName);
            return;
        }

        var artifact = new InvalidatorTemplate(model).RenderOutput();
        ctx.AddSource(artifact);
    }

    private static void GenerateCachingMetadata(
        SourceProductionContext ctx,
        ((ImmutableArray<CacheableModel> Cacheables, ImmutableArray<InvalidatesModel> Invalidators),
            (bool IsDebug, bool HasComposition)) input)
    {
        var ((cacheables, invalidators), (isDebug, hasComposition)) = input;

        if (!hasComposition)
            return;
        if (cacheables.IsDefaultOrEmpty && invalidators.IsDefaultOrEmpty)
            return;

        var artifact = new CachingMetadataTemplate(cacheables, invalidators, isDebug).RenderOutput();
        ctx.AddSource(artifact);
    }

    // =========================================================================
    // Validation helpers
    // =========================================================================

    private static bool IsValidDuration(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return false;
        if (DurationRegex.IsMatch(duration))
            return true;
        return TimeSpan.TryParse(duration, out _);
    }

    private static void ValidateTagPlaceholders(SourceProductionContext ctx, CacheableModel model)
    {
        var propertyNames = new HashSet<string>(model.AllPropertyNames);
        foreach (var tag in model.Tags)
            foreach (Match match in TagPlaceholderRegex.Matches(tag))
            {
                var placeholder = match.Groups[1].Value;
                if (!propertyNames.Contains(placeholder))
                    ctx.ReportDiagnostic(CachingDiagnostics.InvalidPlaceholder,
                        model.Location?.ToLocation(), placeholder, model.TypeName);
            }
    }

    private static void ValidateDuplicateOrders(SourceProductionContext ctx, CacheableModel model)
    {
        var orderGroups = model.KeyProperties.GroupBy(p => p.Order).Where(g => g.Count() > 1);
        foreach (var group in orderGroups)
        {
            var names = group.Select(p => p.Name).ToArray();
            for (var i = 0; i < names.Length - 1; i++)
                ctx.ReportDiagnostic(CachingDiagnostics.DuplicateOrder,
                    model.Location?.ToLocation(), names[i], names[i + 1], group.Key);
        }
    }
}
