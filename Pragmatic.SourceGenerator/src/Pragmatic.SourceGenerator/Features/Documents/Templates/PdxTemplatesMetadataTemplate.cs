using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Documents.Templates;

/// <summary>
///     Emits <c>_Metadata.PdxTemplates.g.cs</c>: this assembly embeds templates, reachable through the
///     anchor type <c>[assembly: PdxTemplates&lt;TAnchor&gt;]</c> named.
/// </summary>
internal sealed class PdxTemplatesMetadataTemplate(string anchor) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Documents";

    protected override string? TriggerInfo => "[assembly: PdxTemplates<TAnchor>]";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload(string anchor)
        => $$"""
             {
               "generator": "Pragmatic.SourceGenerator/Documents",
               "anchor": "{{anchor}}"
             }
             """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("PdxTemplates"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata(MetadataCategory.PdxTemplates, \"1.0\", \"\"\"");
        AppendLine(Payload(anchor));
        AppendLine("\"\"\")]");
    }
}
