using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Documents.Templates;

namespace Pragmatic.SourceGenerator.Features.Documents;

/// <summary>
///     Publishes that an assembly embeds document or mail templates, so the host registers it as a
///     template source instead of each host writing <c>AddPdxTemplates</c> for each module it includes.
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
