using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Diagnostics;
using Pragmatic.SourceGenerator.Features.Mapping.Models;
using Pragmatic.SourceGenerator.Features.Mapping.Templates;
using Pragmatic.SourceGenerator.Features.Mapping.Transforms;

namespace Pragmatic.SourceGenerator.Features.Mapping;

/// <summary>
///     Mapping feature module for the unified Pragmatic source generator.
///     Generates:
///     <list type="bullet">
///         <item>Partial class with FromEntity(), ToEntity(), ApplyTo(), Selector, Projection</item>
///         <item>Extension methods class (To{Dto}(), To{Dto}(IEnumerable), etc.)</item>
///     </list>
/// </summary>
internal static class MappingFeature
{
    /// <param name="context">The generator initialization context.</param>
    /// <param name="features">Which Pragmatic packages the compilation references.</param>
    /// <param name="programmaticMappings">
    ///     Mappings another feature describes as models rather than reading from syntax — the DTOs
    ///     <c>[Resource]</c> generates. This feature is syntax-driven, so a type this generator creates
    ///     has no symbol here and would otherwise need its mapping written a second time.
    /// </param>
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<MappingModel>>? programmaticMappings = null)
    {
        // Register [MapFrom<T>] types
        var mapFromProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MapFrom,
                IsTypeDeclaration,
                MappingTransform.TransformMapFrom)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.MappingMapFrom);

        // Register [MapTo<T>] types
        var mapToProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MapTo,
                IsTypeDeclaration,
                MappingTransform.TransformMapTo)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.MappingMapTo);

        // A mutation IS a [MapTo<TEntity>]: a set of properties written onto an entity. Read here, it
        // inherits converters, renames and dotted targets instead of having a second mapper rewrite
        // them. The model carries IsMutationBody, which limits emission to ApplyToLoaded —
        // WrittenNavigations belongs to Actions, and two generators emitting the same member would
        // give CS0102.
        var mutationProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Mutation,
                IsTypeDeclaration,
                MappingTransform.TransformMutation)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutputSafe(
            mutationProvider.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasMapping)
                    return;
                GenerateSource(ctx, pair.Left with { HasEfCore = pair.Right.HasEfCore });
            });

        // Combine and deduplicate (a type might have both [MapFrom] and [MapTo])
        var allMappings = mapFromProvider
            .Collect()
            .Combine(mapToProvider.Collect())
            .SelectMany(static (pair, _) => MergeMappings(pair.Left, pair.Right))
            .WithTrackingName(TrackingNames.MappingAllMappings);

        // Generate source for each mapping, guarded by feature detection
        context.RegisterSourceOutputSafe(
            allMappings.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasMapping)
                    return;
                GenerateSource(ctx, pair.Left with { HasEfCore = pair.Right.HasEfCore });
            });

        // The same rendering for the DTOs another feature generates. Not guarded on HasMapping: a
        // module can scaffold a resource without referencing Pragmatic.Mapping, and its read DTO still
        // needs the FromEntity and Projection that the query pipeline and the write endpoints call.
        if (programmaticMappings is not null)
        {
            context.RegisterSourceOutputSafe(
                programmaticMappings.Value.SelectMany(static (models, _) => models),
                static (ctx, model) => GenerateSource(ctx, model));
        }

        // PRAG0310: [GenerateProjection] on a type WITHOUT [MapFrom] — invisible to the providers
        // above (they trigger on MapFrom/MapTo), so it gets its own hook.
        var orphanProjections = context.SyntaxProvider
            .ForAttributeWithMetadataName<(string Name, LocationInfo? Location)?>(
                "Pragmatic.Mapping.Attributes.GenerateProjectionAttribute",
                IsTypeDeclaration,
                static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol symbol)
                        return null;
                    var hasMapFrom = symbol.GetAttributes().Any(a =>
                        a.AttributeClass?.OriginalDefinition.ToDisplayString()
                            .StartsWith("Pragmatic.Mapping.Attributes.MapFromAttribute") == true);
                    return hasMapFrom
                        ? null
                        : (Name: symbol.Name, Location: MappingTransform.TypeLocation(ctx.TargetNode));
                })
            .Where(static orphan => orphan is not null);

        context.RegisterSourceOutputSafe(
            orphanProjections.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasMapping)
                    return;
                var orphan = pair.Left!.Value;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ProjectionRequiresMapFrom,
                    orphan.Location?.ToLocation(),
                    orphan.Name));
            });
    }

    /// <summary>Where a diagnostic about the type points: at the type's declaration.</summary>
    private static Location? At(MappingModel model) => model.Location?.ToLocation();

    /// <summary>
    ///     Where a diagnostic about a property points: at the property, or at the type when the
    ///     property was not built from a symbol.
    /// </summary>
    private static Location? At(PropertyMappingModel prop, MappingModel model)
        => prop.Location?.ToLocation() ?? model.Location?.ToLocation();

    private static bool IsTypeDeclaration(SyntaxNode node, CancellationToken _)
    {
        return node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax;
    }

    private static ImmutableArray<MappingModel> MergeMappings(
        ImmutableArray<MappingModel> mapFrom,
        ImmutableArray<MappingModel> mapTo)
    {
        var result = new Dictionary<string, MappingModel>();

        foreach (var model in mapFrom)
            result[model.FullTypeName] = model;

        foreach (var model in mapTo)
            if (result.TryGetValue(model.FullTypeName, out var existing))
                // Merge: type has both [MapFrom] and [MapTo]. Preserve the write-side
                // property model (and mutation flag) so ToEntity/ApplyTo keep [MapTo] semantics
                // instead of silently reusing the read-side [MapFrom] properties.
                result[model.FullTypeName] = existing with
                {
                    HasMapTo = true,
                    HasMutationAttribute = model.HasMutationAttribute,
                    TargetTypeFullName = model.TargetTypeFullName,
                    TargetTypeName = model.TargetTypeName,
                    TargetTypeIsValueType = model.TargetTypeIsValueType,
                    ConstructorParameters = model.ConstructorParameters,
                    HasExplicitConstructor = model.HasExplicitConstructor,
                    // ⚠️ Named explicitly, like everything else here: this merge keeps the READ model
                    // and copies only what is listed, so a write-side fact left out of the list stays
                    // at its default and the write silently takes the other branch. That is the shape
                    // that hid PRAG0333 for a bidirectional DTO.
                    TargetHasParameterlessFactory = model.TargetHasParameterlessFactory,
                    WriteProperties = model.Properties,
                    // Computed by the [MapTo] side over the write model, so it survives the merge
                    // alongside the properties it was derived from.
                    WrittenNavigations = model.WrittenNavigations
                };
            else
                result[model.FullTypeName] = model;

        return result.Values.ToImmutableArray();
    }

    private static void GenerateSource(SourceProductionContext context, MappingModel model)
    {
        // PRAG0338: the attribute names a type argument that is not a type.
        if (model.UnusableGenericArgument)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MappingDiagnostics.UnusableGenericArgument,
                At(model),
                model.TypeName, model.HasMapFrom ? "MapFrom" : "MapTo"));
            return;
        }

        // Not partial: nothing to generate into. PRAG0300 is the companion analyzer's.
        if (model.MissingPartial)
            return;

        // PRAG0313: Circular reference detected (info)
        if (model.HasCircularReferences)
            context.ReportDiagnostic(Diagnostic.Create(
                MappingDiagnostics.CircularReferenceDetected,
                At(model),
                model.TypeName));

        // PRAG0319: CustomizeMapping ignored in projection (type-level)
        if (model.GenerateProjection && model.HasCustomizeMapping)
            context.ReportDiagnostic(Diagnostic.Create(
                MappingDiagnostics.CustomizeMappingIgnoredInProjection,
                At(model),
                model.TypeName));

        // PRAG0316: the selected [MapTo] constructor has a required parameter (no explicit
        // default, non-nullable) with no matching DTO property. Generating `default` for it would
        // silently build an invalid entity, so reject it instead of substituting a default.
        if (model is { HasMapTo: true, ConstructorParameters.IsDefaultOrEmpty: false }
            && model.ConstructorParameters.Any(p =>
                p.MatchingPropertyName is null && !p.IsOptional && !p.IsNullable))
            context.ReportDiagnostic(Diagnostic.Create(
                MappingDiagnostics.NoSuitableConstructor,
                At(model),
                model.TargetTypeName ?? model.TypeName));

        // PRAG0302: a [MapProperty(Target = "...")] segment doesn't exist on the entity (write side).
        // Reported from the write model (EffectiveWriteProperties) and suppressing the generic 0303
        // below — the invalid segment is the precise cause.
        if (model.HasMapTo)
            foreach (var prop in model.EffectiveWriteProperties)
                if (prop.TargetPathInvalidSegment is not null)
                    context.ReportDiagnostic(Diagnostic.Create(
                        MappingDiagnostics.PropertyNotFound,
                        At(prop, model),
                        prop.TargetPathInvalidSegment, model.TargetTypeName ?? "unknown"));

        // PRAG0333: a keyed collection update with nothing to match elements by.
        //
        // EffectiveWriteProperties, not Properties. The template renders ApplyTo from the write list,
        // and for a DTO carrying both [MapFrom] and [MapTo] the merge keeps the READ list in
        // Properties — so a check that iterated Properties inspected a model the write side never
        // used, and said nothing while the template emitted the constant selector `d => 0`. The
        // failure then arrived as a DuplicateMappingKeyException on the first update carrying two
        // children. Measured on examples/conformance, where OrderLineDto is bidirectional; every
        // existing test declares [MapTo] alone, which is why the two lists coincided and the check
        // appeared to work.
        foreach (var prop in model.EffectiveWriteProperties)
            if (prop.CollectionWrite is { KeyProblem: not CollectionKeyProblem.None } collection)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.CollectionElementsCannotBeMatched,
                    At(prop, model),
                    prop.PropertyName, model.TypeName, collection.Strategy,
                    collection.KeyProblem == CollectionKeyProblem.KeyNotOnBothSides
                        ? $"the child is keyed by '{collection.EntityKeyProperty}' and the element DTO does not carry it"
                        : "neither side offers a key"));

        // PRAG0336: a write path pointed at a property the entity computes.
        foreach (var prop in model.EffectiveWriteProperties)
            if (prop is { TargetIsReadOnly: true, IsIgnored: false })
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.TargetPropertyIsReadOnly,
                    At(prop, model),
                    model.TypeName, prop.PropertyName, model.TargetTypeName ?? "unknown"));

        // PRAG0335: [LinkIds] where no tracked write exists to carry it out.
        if (!model.HasEfCore)
            foreach (var prop in model.EffectiveWriteProperties)
                if (prop.LinkNavigation is { Length: > 0 } navigation)
                    context.ReportDiagnostic(Diagnostic.Create(
                        MappingDiagnostics.LinkIdsNeedsTheTrackedForm,
                        At(prop, model),
                        model.TypeName, prop.PropertyName, navigation));

        // PRAG0325 (Hidden): reverse coverage — source properties the DTO silently drops.
        foreach (var unmapped in model.UnmappedSourceProperties)
            context.ReportDiagnostic(Diagnostic.Create(
                MappingDiagnostics.UnmappedSourceProperty,
                At(model),
                model.SourceTypeName ?? "unknown", unmapped, model.TypeName));

        // PRAG0330: [MapDerived] pairs violating the inheritance contract.
        foreach (var invalidDerived in model.InvalidDerivedMappings)
            context.ReportDiagnostic(Diagnostic.Create(
                MappingDiagnostics.InvalidDerivedMapping,
                At(model),
                model.TypeName, invalidDerived));

        // PRAG0331 (Info): [MapDerived] dispatch is runtime-only; projections keep the base shape.
        if (model.GenerateProjection && model.DerivedMappings.Length > 0)
            context.ReportDiagnostic(Diagnostic.Create(
                MappingDiagnostics.DerivedMappingIgnoredInProjection,
                At(model),
                model.TypeName));

        // Per-property diagnostics
        var sourceName = model.SourceTypeName ?? model.TargetTypeName ?? "unknown";
        foreach (var prop in model.Properties)
        {
            // PRAG0302: an explicit [MapProperty("...")] source path doesn't resolve. More precise
            // than the generic 0303, which is suppressed for this property.
            // PRAG0334 takes its place where the reason is a boundary: both report the same absence,
            // and only one of them stops the reader looking for a typo that is not there.
            if (prop.ExplicitSourcePathInvalid is not null)
                context.ReportDiagnostic(prop.CrossBoundaryOtherEntity is not null
                    ? Diagnostic.Create(
                        MappingDiagnostics.PropertyCrossesABoundary,
                        At(prop, model),
                        prop.ExplicitSourcePathInvalid,
                        prop.CrossBoundaryOtherEntity,
                        prop.CrossBoundaryOwn,
                        prop.CrossBoundaryOther)
                    : Diagnostic.Create(
                        MappingDiagnostics.PropertyNotFound,
                        At(prop, model),
                        prop.ExplicitSourcePathInvalid, sourceName));

            // PRAG0314: [MapIgnore] + [MapProperty] on the same property is a contradiction.
            if (prop.HasConflictingAttributes)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ConflictingAttributes,
                    At(prop, model),
                    prop.PropertyName));

            // PRAG0305/PRAG0306: [MapConverter] contract violations — without these the failure is a
            // cryptic CS error inside generated code.
            if (prop.ConverterMissingInterface)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ConverterMustImplementInterface,
                    At(prop, model),
                    prop.ConverterType ?? "converter", prop.SourcePropertyType ?? "TSource", prop.PropertyType));
            if (prop.ConverterMissingParameterlessCtor)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ConverterMustHaveParameterlessConstructor,
                    At(prop, model),
                    prop.ConverterType ?? "converter"));

            // PRAG0315: nested DTO declares a [MapFrom<T>] unrelated to the actual navigation type.
            if (prop.NestedDtoMismatchSource is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.NestedDtoMismatch,
                    At(prop, model),
                    prop.NestedDtoType ?? prop.PropertyType, prop.PropertyName));

            // PRAG0323: direct name match AND a flattening convention both apply — direct wins, warn.
            if (prop.AmbiguousWithConvention)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.AmbiguousMapping,
                    At(prop, model),
                    prop.PropertyName));

            // PRAG0304: clearly incompatible simple types with no conversion path (e.g. Guid → bool).
            // Conservative: only known simple types, so inheritance/user-conversions can't false-positive.
            if (Analysis.TypeConversionHelper.IsKnownIncompatible(prop))
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.IncompatibleTypes,
                    At(prop, model),
                    prop.SourcePropertyType!, prop.PropertyType));

            // PRAG0317: nullable source → non-nullable target with no Default and no auto-default
            // (target is not a simple/enum type, so Defaults.cs cannot synthesize one).
            if (prop is { SourceIsNullable: true, IsNullable: false, DefaultValue: null, IsNestedDto: false,
                    CollectionKind: CollectionKind.None, IsDictionary: false, HasConverter: false,
                    Conversion: ConversionKind.None, TargetIsEnum: false, Resolution: not MappingResolution.None }
                && !Analysis.KnownTypes.SimpleTypes.Contains(prop.PropertyType.TrimEnd('?')))
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.NullableWithoutDefault,
                    At(prop, model),
                    prop.SourcePropertyType ?? "source", prop.PropertyType));

            // PRAG0341 (Info): the complement of PRAG0317. Where the target IS a simple or enum type the
            // mapping does not refuse — it synthesises default(T) — and without this would say nothing,
            // so a DateTimeOffset over a nullable UpdatedAt would ship 0001-01-01 as if it were a date.
            if (prop is { SourceIsNullable: true, IsNullable: false, DefaultValue: null, IsNestedDto: false,
                    CollectionKind: CollectionKind.None, IsDictionary: false, HasConverter: false,
                    Conversion: ConversionKind.None, Resolution: not MappingResolution.None }
                && (prop.TargetIsEnum || Analysis.KnownTypes.SimpleTypes.Contains(prop.PropertyType.TrimEnd('?')))
                && !Analysis.TypeConversionHelper.IsKnownIncompatible(prop))
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.NullableSourceDefaulted,
                    At(prop, model),
                    prop.PropertyName, prop.SourcePropertyType ?? "source", prop.PropertyType));

            // PRAG0328: enum→enum by-name mapping with a source member missing on the target.
            if (prop.EnumMemberMismatch is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.EnumMemberMissing,
                    At(prop, model),
                    prop.PropertyName, prop.EnumMemberMismatch, prop.PropertyType));

            // PRAG0329: [MapCondition] predicate missing or wrong shape.
            if (prop.ConditionMethodInvalid)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ConditionMethodInvalid,
                    At(prop, model),
                    prop.PropertyName, prop.ConditionMethod ?? "?", model.TypeName));

            // PRAG0332 (Info): [MapCondition] gates FromEntity only — the projection is unconditional.
            // Not when the predicate's body was inlined into the projection.
            if (model.GenerateProjection
                && prop is { ConditionMethod: not null, ConditionMethodInvalid: false, ConditionProjectionBody: null })
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ConditionIgnoredInProjection,
                    At(prop, model),
                    prop.PropertyName, model.TypeName));

            // PRAG0340 (Info): mapped, and the projection cannot carry it. The condition mirrors the
            // filter in MappingTemplate.RenderProjection — if the two drift, this says nothing about
            // the property that actually went missing.
            if (model.GenerateProjection && !prop.IsIgnored
                && prop.Resolution != MappingResolution.None
                && !prop.IsSqlTranslatable
                && !prop.IsComputedAfterTheRead
                && !(prop.IsNestedDto && prop.NestedProjectionMappings.Length > 0)
                && !(prop.IsElementDto && prop.ElementProjectionMappings.Length > 0))
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.PropertyDroppedFromProjection,
                    At(prop, model),
                    prop.PropertyName, model.TypeName));

            // PRAG0303: Unmapped properties warning
            if (prop.Resolution == MappingResolution.None && !prop.IsIgnored
                && prop.TargetPathInvalidSegment is null && prop.ExplicitSourcePathInvalid is null
                && prop.EnumMemberMismatch is null)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.NoMatchingSourceProperty,
                    At(prop, model),
                    prop.PropertyName, sourceName));

            // PRAG0307: Required property not mapped
            if (prop is { IsRequired: true, Resolution: MappingResolution.None, IsIgnored: false })
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.RequiredPropertyNotMapped,
                    At(prop, model),
                    prop.PropertyName, model.TypeName));

            // PRAG0309: Nested DTO missing [MapFrom]
            if (prop is { IsNestedDto: true, NestedDtoType: not null } && model.HasMapFrom)
                if (prop.NestedProjectionMappings.IsDefaultOrEmpty && !prop.HasConverter)
                    context.ReportDiagnostic(Diagnostic.Create(
                        MappingDiagnostics.NestedTypeMissingMapFrom,
                        At(prop, model),
                        prop.NestedDtoType, prop.PropertyName));

            // PRAG0320: [MapConverter] not supported in projection
            if (model.GenerateProjection && prop is { HasConverter: true, IsSqlTranslatable: false, IsComputedAfterTheRead: false })
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ConverterNotSupportedInProjection,
                    At(prop, model),
                    prop.PropertyName));

            // PRAG0321: Format not supported in projection
            if (model.GenerateProjection && prop is { Format: not null, IsComputedAfterTheRead: false })
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.FormatNotSupportedInProjection,
                    At(prop, model),
                    prop.PropertyName, model.TypeName));

            // PRAG0326: nested DTO inlining dropped a complex mapping in projection
            if (model.GenerateProjection && prop.HasDroppedNestedProjectionMappings)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.NestedProjectionMappingDropped,
                    At(prop, model),
                    prop.PropertyName, model.TypeName));

            // PRAG0327: nested projection truncated by the MaxDepth cap
            if (model.GenerateProjection && prop.ProjectionDepthCapped)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ProjectionDepthExceeded,
                    At(prop, model),
                    prop.PropertyName, model.TypeName, model.ProjectionMaxDepth));

            // PRAG0322: Complex dictionary not supported
            if (prop is { IsDictionary: true, IsDictionaryValueSimple: false })
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.ComplexDictionaryNotSupported,
                    At(prop, model),
                    prop.PropertyName, model.TypeName));

            // PRAG0324: Id property excluded from ToEntity (info)
            if (prop is { IsIdProperty: true, ForceIncludeId: false } && model.HasMapTo)
                context.ReportDiagnostic(Diagnostic.Create(
                    MappingDiagnostics.IdPropertyExcluded,
                    At(prop, model),
                    prop.PropertyName, model.TypeName));
        }

        // Generate main mapping partial class
        var mainTemplate = new MappingTemplate(model);
        var mainArtifact = mainTemplate.RenderOutput();
        if (!mainArtifact.IsEmpty)
            context.AddSource(mainArtifact);

        // Generate extension methods class
        if (model.HasMapFrom)
        {
            var extensionsTemplate = new MappingExtensionsTemplate(model);
            var extArtifact = extensionsTemplate.RenderOutput();
            if (!extArtifact.IsEmpty)
                context.AddSource(extArtifact);
        }
    }

}
