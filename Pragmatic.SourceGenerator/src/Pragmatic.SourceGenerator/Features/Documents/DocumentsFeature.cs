using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Documents.Models;
using Pragmatic.SourceGenerator.Features.Documents.Templates;
using Pragmatic.SourceGenerator.Features.Documents.Transforms;

namespace Pragmatic.SourceGenerator.Features.Documents;

/// <summary>
///     Publishes that an assembly embeds document or mail templates, so the host registers it as a
///     template source instead of each host writing <c>AddPdxTemplates</c> for each module it includes;
///     and writes the property accessors of the typed data the assembly gives a template.
/// </summary>
/// <remarks>
///     <para>
///         The declaration is <c>[assembly: PdxTemplates&lt;TAnchor&gt;]</c>, explicit on purpose: the
///         templates are <c>EmbeddedResource</c> items, which a generator does not see, and guessing from a
///         file naming convention is the implicit wiring the repository refuses.
///     </para>
///     <para>
///         Two channels, as for every declaration the host composes: the assembly attribute this emits is
///         how a <b>referenced module</b> is found, and the entry returned here is how the <b>host's own</b>
///         declaration reaches the same aggregation.
///     </para>
/// </remarks>
internal static class DocumentsFeature
{
    private const string AttributeName = "Pragmatic.Documents.Markup.PdxTemplatesAttribute<TAnchor>";

    public static IncrementalValueProvider<EquatableArray<MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context)
    {
        RegisterTemplateAccessors(context);

        var anchor = context.CompilationProvider.Select(static (compilation, _) => AnchorOf(compilation));

        context.RegisterSourceOutputSafe(anchor, static (ctx, type) =>
        {
            if (type is not null)
                ctx.AddSource(new PdxTemplatesMetadataTemplate(type).RenderOutput());
        });

        return anchor.Select(static (type, _) => type is null
            ? EquatableArray<MetadataEntry>.Empty
            : new EquatableArray<MetadataEntry>(ImmutableArray.Create(
                HostLocalRegistration.CreatePayload(
                    MetadataCategoryIds.PdxTemplates,
                    "1.0",
                    PdxTemplatesMetadataTemplate.Payload(type)))));
    }

    /// <summary>
    ///     Writes the property accessors of the types this assembly gives a template, so the data context
    ///     reads them without reflection, and under Native AOT reads them at all.
    /// </summary>
    /// <remarks>
    ///     Driven by the call sites rather than by a <c>DetectedFeatures</c> flag: a call can only bind a
    ///     <c>[TemplateData]</c> parameter when Templating is referenced, so finding one is the condition.
    ///     One file per assembly, the types sorted, so the output does not depend on the order the call
    ///     sites are visited.
    /// </remarks>
    private static void RegisterTemplateAccessors(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => TemplateDataTypeReader.IsCandidate(node),
                static (ctx, ct) => TemplateDataTypeReader.Read(ctx, ct))
            .Where(static models => models.Length > 0)
            .Collect()
            .Select(static (all, _) => new EquatableArray<TemplateDataTypeModel>(all
                .SelectMany(static models => models.AsImmutableArray())
                .GroupBy(static model => model.FullyQualifiedName, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static model => model.FullyQualifiedName, StringComparer.Ordinal)
                .ToImmutableArray()));

        var assemblyName = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? "Global");

        context.RegisterSourceOutputSafe(types.Combine(assemblyName), static (ctx, pair) =>
        {
            var (models, assembly) = pair;
            if (models.Length == 0)
                return;

            ctx.AddSource(new TemplateAccessorsTemplate(
                GeneratedRegistrationNames.InGeneratedNamespace(assembly), models).RenderOutput());
        });
    }

    /// <summary>The anchor type <c>[assembly: PdxTemplates&lt;TAnchor&gt;]</c> names, or <c>null</c>.</summary>
    private static string? AnchorOf(Compilation compilation)
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true } type
                || type.OriginalDefinition.ToDisplayString() != AttributeName)
                continue;

            if (type.TypeArguments[0] is INamedTypeSymbol { TypeKind: not TypeKind.Error } anchor)
                return anchor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
    }
}
