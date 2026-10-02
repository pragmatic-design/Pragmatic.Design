using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     Template for generating the main mapping partial class.
///     Generates FromEntity(), ToEntity(), and Projection.
/// </summary>
internal sealed partial class MappingTemplate : CSharpTemplate
{
    private readonly MappingModel _model;

    public MappingTemplate(MappingModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Mapping";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => _model is { HasMapFrom: true, HasMapTo: true } ? $"[MapFrom/MapTo] on {_model.TypeName}" : _model.HasMapFrom ? $"[MapFrom] on {_model.TypeName}" : $"[MapTo] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Mapping", _model.Namespace),
            ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        // Note: We use global::Pragmatic.Ensure.Ensure to avoid namespace/class collision
        // The namespace Pragmatic.Ensure contains a class also named Ensure

        if (_model.GenerateProjection)
            AddUsing("System.Linq.Expressions");

        // ToImmutableArray()/ToImmutableList() materializers need the Immutable namespace.
        if (_model.Properties.Concat(_model.EffectiveWriteProperties.AsImmutableArray()).Any(p =>
                p.CollectionKind is CollectionKind.ImmutableArray or CollectionKind.ImmutableList
                || p.TargetCollectionKind is CollectionKind.ImmutableArray or CollectionKind.ImmutableList))
            AddUsing("System.Collections.Immutable");

        AppendNamespace(_model.Namespace);
        AppendLine();

        // Main partial type - must match the original declaration
        RenderPartialType();
    }

    private void RenderPartialType()
    {
        var accessibility = TemplateHelpers.ParseAccessibility(_model.Accessibility);
        var mods = new ClassModifiers { Partial = true };

        // Generate the correct type kind to match the original declaration
        switch (_model.TypeKind)
        {
            case "record":
                Record(_model.TypeName, RenderBody,
                    null,
                    accessModifier: accessibility,
                    modifiers: mods);
                break;

            case "record struct":
                RecordStruct(_model.TypeName, RenderBody,
                    null,
                    accessModifier: accessibility,
                    modifiers: mods);
                break;

            case "struct":
                Struct(_model.TypeName, RenderBody,
                    null,
                    accessibility,
                    mods);
                break;

            default: // "class"
                Class(_model.TypeName, RenderBody,
                    null,
                    null,
                    accessibility,
                    mods);
                break;
        }
    }

    /// <summary>
    ///     Whether the target can only be built, never updated in place.
    /// </summary>
    /// <remarks>
    ///     True when the write reaches at least one <c>init</c> member: an object initializer is the
    ///     only place that can be written, so construction is the only form that exists. Asked on the
    ///     write list rather than on all properties, because a read-only property the write path
    ///     already skips says nothing about this.
    /// </remarks>
    private bool TargetIsBuildOnly()
        => _model.EffectiveWriteProperties.Any(p =>
            p is { IsIgnored: false, TargetIsInitOnly: true, TargetIsReadOnly: false });

    private void RenderBody()
    {
        RenderConverterFields();

        if (_model.HasMapFrom)
        {
            RenderFromEntity();
            if (_model.GenerateBodyOnlyVariant)
                RenderFromEntityBodyOnly();

            // Partial methods are always generated (C# partial methods are no-op when not implemented)
            // Context struct is always needed by the FromEntity flow
            RenderPartialMethods();
            RenderContextStruct();
        }

        // A mutation: only the body of the write. The invoker loads the entity, so ToEntity has no
        // caller; ApplyTo and the tracked shape are for DTOs someone applies by hand;
        // WrittenNavigations is emitted by Actions, which composes the children's lists.
        if (_model.IsMutationBody)
        {
            RenderApplyToLoaded();
            RenderWritePartialMethods();
            return;
        }

        if (_model.HasMapTo)
        {
            RenderToEntity();
            // Skip ApplyTo if DTO also has [Patch<T>] - Patch generator handles update semantics.
            //
            // ⚠️ And skip it for a target that cannot be applied to at all. A type whose properties
            // are `init` — a mutation, a record built once — can be constructed and never updated:
            // `entity.X = …` on one is CS8852 inside a generated file. It is what a DTO mapping onto
            // a mutation hit, and the answer is not to write the assignment differently but to not
            // offer a method whose whole meaning is "change what is already there". PRAG0337 says so.
            if (!_model.HasMutationAttribute && !TargetIsBuildOnly())
            {
                // The body, under the name that states its precondition. Always present, so that
                // generated code which cannot see this model — ChildWritingTemplate, writing a
                // mutation's context-free ApplyToEntity override — can name it unconditionally.
                RenderApplyToLoaded();

                // The plain form survives only where it cannot be wrong: no EF, or no navigation
                // written. It returns without emitting anything otherwise.
                RenderApplyTo();

                // Only where EF is present: the form exists to ask EF a question, and without EF
                // there is nobody to ask.
                if (_model.HasEfCore)
                    RenderApplyToWithContext();
            }

            RenderWritePartialMethods();
        }
        if (_model.HasMapFrom)
            RenderSelector();
        if (_model is { GenerateProjection: true, HasMapFrom: true })
            RenderProjection();
        // Unconditional on a [MapFrom] DTO, empty list included. Emitted only when there is something
        // to put in it, it would be unusable from generated code: a caller would have to know whether
        // the member exists before naming it, and a generator cannot see what another generator wrote. Always present means the mutation invoker can just iterate it.
        if (_model.HasMapFrom)
            RenderRequiredNavigations();

        // On every [MapTo], empty included: the mutation invoker names it for each writable child and
        // cannot check that it exists first.
        //
        // ⚠️ Except where the type also carries [Patch<T>]. The patch template publishes the same
        // member — deep and prefixed, composed from what its children write — and two generators
        // emitting one member on one partial class is CS0102 inside a file the author cannot open.
        // Same silence as on a mutation body, where Actions composes the list; the patch's is the
        // informed version here.
        if (_model is { HasMapTo: true, HasMutationAttribute: false })
            RenderWrittenNavigations();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Shared Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     One cached instance per distinct [MapConverter] type (converters are stateless by contract).
    ///     Read/write expressions use these instead of allocating <c>new TConverter()</c> per property use.
    /// </summary>
    private void RenderConverterFields()
    {
        var converters = _model.Properties
            .Concat(_model.EffectiveWriteProperties.AsImmutableArray())
            .Where(p => p.HasConverter)
            .Select(p => p.ConverterType!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (converters.Count == 0)
            return;

        Comment("Cached converter instances (stateless by IValueConverter contract)");
        foreach (var converter in converters)
            AppendLine($"private static readonly {converter} {ConverterFieldName(converter)} = new();");
        AppendLine();

        RenderConvertersAfterTheRead();
    }

    /// <summary>
    ///     A static method per converted member the projection carries, which calls the cached converter.
    /// </summary>
    /// <remarks>
    ///     The projection cannot name the cached instance itself: EF Core reads a static field as a
    ///     constant, and refuses a client projection that calls an instance method on one — it would keep
    ///     the object alive in its query cache. A static method holds no instance for it to capture.
    /// </remarks>
    private void RenderConvertersAfterTheRead()
    {
        if (!_model.GenerateProjection)
            return;

        var converted = _model.Properties
            .Where(p => p is { IsComputedAfterTheRead: true, HasConverter: true, SourcePropertyType: not null })
            .ToList();
        if (converted.Count == 0)
            return;

        Comment("Called by Projection, which computes these members on the client after the read");
        foreach (var prop in converted)
            AppendLine($"private static {prop.PropertyType} {ConvertAfterTheReadName(prop)}({prop.SourcePropertyType} source) "
                       + $"=> {ConverterFieldName(prop.ConverterType!)}.Convert(source);");
        AppendLine();
    }

    /// <summary>The static method the projection calls for a converted member.</summary>
    internal static string ConvertAfterTheReadName(PropertyMappingModel prop) => $"ConvertAfterTheRead_{prop.PropertyName}";

    /// <summary>Deterministic field name for a converter type (namespace-qualified, sanitized).</summary>
    internal static string ConverterFieldName(string converterType)
    {
        var sanitized = converterType
            .Replace("global::", "")
            .Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');
        return $"_converter_{sanitized}";
    }

    /// <summary>
    ///     Returns properties that are body-only: not ignored, resolved, and scalar
    ///     (no collections, nested DTOs, or dictionaries).
    /// </summary>
    private IEnumerable<PropertyMappingModel> GetBodyOnlyProperties()
    {
        return _model.Properties.Where(p =>
            !p.IsIgnored &&
            p.Resolution != MappingResolution.None &&
            p is { CollectionKind: CollectionKind.None, IsNestedDto: false, IsDictionary: false });
    }
}
