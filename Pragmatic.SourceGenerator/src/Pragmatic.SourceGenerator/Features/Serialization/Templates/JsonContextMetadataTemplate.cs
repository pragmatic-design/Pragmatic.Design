using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Serialization.Templates;

/// <summary>
///     Emits <c>_Metadata.JsonContexts.g.cs</c> — assembly metadata so the Composition host aggregates
///     and calls the generated context's registration. Category 20 = JsonContexts (cast, as the runtime
///     enum stops at Sagas=17 — same pattern as TraitEntities=19).
/// </summary>
internal sealed class JsonContextMetadataTemplate : CSharpTemplate
{
    private readonly string _namespace;

    public JsonContextMetadataTemplate(string ns) => _namespace = ns;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Serialization";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("JsonContexts"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        var registrationFqn = JsonEscape(GeneratedRegistrationNames.JsonContextFqn(_namespace));

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)20 /* JsonContexts */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Serialization\",");
        AppendLine($"\"registrationMethod\": \"{registrationFqn}\"");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
