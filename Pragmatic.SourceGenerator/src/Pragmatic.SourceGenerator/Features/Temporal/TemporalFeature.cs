using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Temporal.Diagnostics;
using Pragmatic.SourceGenerator.Features.Temporal.Models;
using Pragmatic.SourceGenerator.Features.Temporal.Templates;
using Pragmatic.SourceGenerator.Features.Temporal.Transforms;

namespace Pragmatic.SourceGenerator.Features.Temporal;

/// <summary>
///     Collects DTO properties annotated with the six timezone conversion attributes and emits
///     the per-assembly <c>TemporalJsonBehaviorRegistry</c> registration (plus Composition
///     metadata so the host aggregates it). Gated on <c>HasTemporalJson</c>.
/// </summary>
internal static class TemporalFeature
{
    private const string SchemaVersion = "1.0";

    /// <returns>
    ///     The behaviour registration this compilation generates, for a host that declares the
    ///     annotated properties itself.
    /// </returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<System.Collections.Immutable.ImmutableArray<Endpoints.Models.EndpointModel>> endpoints)
    {
        RegisterTemporalProperties(context, features);

        var clockBindings = RegisterClockBindings(context, features);

        var asUtc = Pipeline(context, AttributeNames.TemporalAsUtc, "AsUtc");
        var fromClient = Pipeline(context, AttributeNames.TemporalFromClientTimezone, "FromClientTimezone");
        var fromBusiness = Pipeline(context, AttributeNames.TemporalFromBusinessTimezone, "FromBusinessTimezone");
        var toClient = Pipeline(context, AttributeNames.TemporalToClientTimezone, "ToClientTimezone");
        var toBusiness = Pipeline(context, AttributeNames.TemporalToBusinessTimezone, "ToBusinessTimezone");
        var keep = Pipeline(context, AttributeNames.TemporalKeepTimezone, "KeepTimezone");

        var all = asUtc.Collect()
            .Combine(fromClient.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(fromBusiness.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(toClient.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(toBusiness.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(keep.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right));

        // The seventh source, and the one the six above cannot be: an endpoint with body properties
        // deserializes a generated {Trigger}Body record, not the trigger the author wrote the attribute
        // on. The six pipelines register the trigger type — right, and not enough, because nothing
        // deserializes it. This registers the record that is.
        //
        // ⚠️ Emitted here rather than as an attribute on the generated record: a generator does not see
        // its own output, so re-emitting [FromClientTimezone] there would be inert a second time. The
        // same generator writes both, so naming the record is safe.
        all = all.Combine(endpoints).Select(static (pair, _) =>
            pair.Left.AddRange(BodyRecordBehaviors(pair.Right)));

        var withFeatures = all.Combine(features);

        context.RegisterSourceOutputSafe(withFeatures, static (ctx, x) =>
        {
            var (models, detected) = x;
            if (!detected.HasTemporalJson || models.IsDefaultOrEmpty)
                return;

            Generate(ctx, models, detected.HasComposition);
        });

        // Same condition as the metadata emitted in Generate: a host that declares the annotated
        // properties itself never sees that attribute, so it is handed the registration directly.
        var behaviors = withFeatures.Select(static (x, _) =>
        {
            var (models, detected) = x;
            if (!detected.HasTemporalJson || !detected.HasComposition || models.IsDefaultOrEmpty)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            var valid = SupportedOf(models);
            if (valid.IsDefaultOrEmpty)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.Create(
                    Composition.MetadataCategoryIds.TemporalBehaviors,
                    SchemaVersion,
                    TemporalBehaviorsTemplate.RegistrationMethodFqnFor(valid)));
        });

        return behaviors.Combine(clockBindings).Select(static (pair, _) =>
            new EquatableArray<Composition.Models.MetadataEntry>(
                pair.Left.AsImmutableArray().AddRange(pair.Right.AsImmutableArray())));
    }

    /// <summary>
    ///     Says that this assembly takes a value from the clock, so a host can answer for the
    ///     <c>IClock</c> the generated invoker resolves — or say it cannot.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The declaration and the implementation are in different packages: <c>[FromClock]</c> and
    ///     <c>IClock</c> are in Abstractions, which every module has, while <c>SystemClock</c> and
    ///     <c>AddPragmaticTemporal()</c> are in Pragmatic.Temporal, which is not in a host's default
    ///     package set. So a module can declare a binding nobody has undertaken to serve, and without
    ///     this the first evidence would be a 500 on that route. The host reads this and reports
    ///     <c>PRAG1698</c>; it cannot register what it cannot name.
    /// </remarks>
    private static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>>
        RegisterClockBindings(
            IncrementalGeneratorInitializationContext context,
            IncrementalValueProvider<DetectedFeatures> features)
    {
        var declarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.FromClock,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => ctx.TargetSymbol.ContainingType?.Name ?? "")
            .Collect()
            .Combine(features)
            .Select(static (pair, _) => (Count: pair.Left.Length, pair.Right.HasComposition));

        context.RegisterSourceOutputSafe(declarations, static (ctx, data) =>
        {
            if (data.Count == 0 || !data.HasComposition)
                return;

            ctx.AddSource(new ClockBindingsMetadataTemplate(data.Count).RenderOutput());
        });

        return declarations.Select(static (data, _) => data.Count == 0 || !data.HasComposition
            ? EquatableArray<Composition.Models.MetadataEntry>.Empty
            : new EquatableArray<Composition.Models.MetadataEntry>(ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.CreatePayload(
                    Composition.MetadataCategoryIds.ClockBindings,
                    SchemaVersion,
                    ClockBindingsMetadataTemplate.Payload(data.Count)))));
    }

    /// <summary>The properties whose type the behaviour registry can convert — the ones that get emitted.</summary>
    private static ImmutableArray<TemporalBehaviorPropertyModel> SupportedOf(
        ImmutableArray<TemporalBehaviorPropertyModel> models)
        => models.Where(m => m.IsSupportedPropertyType).ToImmutableArray();

    /// <summary>
    ///     Lists each type's temporal properties so the EF conventions stop scanning for them.
    /// </summary>
    /// <remarks>
    ///     Both conventions walked <c>ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)</c>
    ///     per entity at startup, testing every property against a closed set of eight types. The set is
    ///     closed and the properties are visible here, so the list is written rather than discovered.
    ///     <para>
    ///         Every type is considered, not only entities: EF maps types the generator has no attribute
    ///         to recognise, and registering one that never reaches a model costs a dictionary entry.
    ///     </para>
    /// </remarks>
    private static void RegisterTemporalProperties(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var models = context.SyntaxProvider
            .CreateSyntaxProvider(
                // TypeDeclarationSyntax, not ClassDeclarationSyntax: entities are frequently records.
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => TemporalEntityTransform.From(ctx))
            .Where(static model => model is not null);

        context.RegisterSourceOutputSafe(
            models.Combine(features),
            static (ctx, pair) =>
            {
                var (model, detected) = pair;
                if (model is null || !detected.HasTemporalEfCore)
                    return;

                var template = new TemporalPropertiesTemplate(model);
                ctx.AddSource(template.RenderOutput());
            });
    }

    /// <summary>
    ///     The behaviours the generated request-body records carry, from the trigger properties they
    ///     were copied from.
    /// </summary>
    /// <remarks>
    ///     The body property model carries the behaviour because the Endpoints transform is where the
    ///     source symbol is; here it becomes a registration against the record's own type. Types with no
    ///     body, and body properties that declare nothing, contribute nothing.
    /// </remarks>
    private static ImmutableArray<TemporalBehaviorPropertyModel> BodyRecordBehaviors(
        System.Collections.Immutable.ImmutableArray<Endpoints.Models.EndpointModel> endpoints)
    {
        var models = ImmutableArray.CreateBuilder<TemporalBehaviorPropertyModel>();

        foreach (var endpoint in endpoints)
        {
            if (!endpoint.IsValid)
                continue;

            foreach (var variant in endpoint.BodyDtoVariants)
            foreach (var property in variant.Properties)
            {
                if (property.TemporalBehavior is not { } behavior)
                    continue;

                models.Add(new TemporalBehaviorPropertyModel
                {
                    ContainingTypeFqn = string.IsNullOrEmpty(endpoint.Namespace)
                        ? variant.Name
                        : $"{endpoint.Namespace}.{variant.Name}",
                    ContainingNamespace = endpoint.Namespace ?? "",
                    PropertyName = property.Name,
                    Behavior = behavior,
                    // The Endpoints transform only records a behaviour for a property that can carry an
                    // instant, so anything reaching here is supported by construction.
                    IsSupportedPropertyType = true,
                    PropertyTypeDisplay = property.TypeName,
                    Location = endpoint.LocationInfo
                });
            }
        }

        return models.ToImmutable();
    }

    private static IncrementalValuesProvider<TemporalBehaviorPropertyModel> Pipeline(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn,
        string behavior)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                static (node, _) => node is PropertyDeclarationSyntax,
                (ctx, _) => TemporalBehaviorTransform.Transform(ctx, behavior))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);
    }

    private static void Generate(
        SourceProductionContext ctx,
        ImmutableArray<TemporalBehaviorPropertyModel> models,
        bool hasComposition)
    {
        foreach (var invalid in models
                     .Where(m => !m.IsSupportedPropertyType)
                     .OrderBy(m => m.ContainingTypeFqn)
                     .ThenBy(m => m.PropertyName))
        {
            ctx.ReportDiagnostic(TemporalDiagnostics.UnsupportedPropertyType,
                invalid.Location?.ToLocation(),
                invalid.ContainingTypeFqn, invalid.PropertyName, invalid.PropertyTypeDisplay);
        }

        var valid = SupportedOf(models);
        if (valid.IsDefaultOrEmpty)
            return;

        var template = new TemporalBehaviorsTemplate(valid);
        var artifact = template.RenderOutput();
        if (artifact.IsEmpty)
            return;

        ctx.AddSource(artifact);

        if (!hasComposition)
            return;

        var metadata = new TemporalBehaviorsMetadataTemplate(
            template.RegistrationMethodFqn, valid.Length).RenderOutput();
        ctx.AddSource(metadata);
    }
}
