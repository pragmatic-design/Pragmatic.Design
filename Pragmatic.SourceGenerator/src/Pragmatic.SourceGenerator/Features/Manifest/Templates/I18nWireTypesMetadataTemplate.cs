using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     Emits <c>_Metadata.I18nWireTypes.g.cs</c> — how an assembly says one of the internationalization
///     value types travels on its wire.
/// </summary>
/// <remarks>
///     Presence and nothing else, as the resilience and feature-flag documents are: the host does not
///     name the members, it decides one thing from the fact — whether to install the JSON converters
///     where nothing else declared i18n. Without them a request carrying a <c>Money</c> is a 400 before
///     any rule runs, on an application that started perfectly.
/// </remarks>
internal sealed class I18nWireTypesMetadataTemplate : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Manifest";

    protected override string? TriggerInfo => "an internationalization value type on an endpoint's shape";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload()
        => """
            {
              "generator": "Pragmatic.SourceGenerator/Manifest"
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("I18nWireTypes"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)31 /* I18nWireTypes */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Manifest\"");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
