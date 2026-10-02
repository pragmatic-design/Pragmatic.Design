using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Redaction.Templates;

/// <summary>
///     Emits <c>_Metadata.Redaction.g.cs</c> — the assembly attribute by which a module publishes its
///     redaction registration, so the Composition host discovers and calls it.
/// </summary>
/// <remarks>
///     There are two channels and both are needed: the <c>MetadataEntry</c> a feature returns covers the
///     host's OWN compilation, while this attribute is how a referenced module is discovered. Emitting
///     only the first registered the host's map and left every module's unregistered — measured on a
///     real consumer, where one module declared the marked members and its map was the one that
///     stayed out.
/// </remarks>
internal sealed class RedactionMetadataTemplate : CSharpTemplate
{
    private readonly string _assemblyName;
    private readonly string _namespace;

    public RedactionMetadataTemplate(string mapNamespace, string assemblyName)
    {
        _namespace = mapNamespace;
        _assemblyName = assemblyName;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Redaction";
    protected override string? TriggerInfo => "[NotLogged] / [PersonalData]";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Redaction"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        var registrationFqn = JsonEscape(GeneratedRegistrationNames.RedactionFqn(_namespace, _assemblyName));

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)25 /* Redaction */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Redaction\",");
        AppendLine($"\"registrationMethod\": \"{registrationFqn}\"");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
