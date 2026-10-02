using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The assembly attribute that tells a host this module has <c>[RollUp]</c> rules to register, and
///     what to call.
/// </summary>
/// <remarks>
///     The host reads metadata off its <b>references</b> only, so a module has to publish this for its
///     rules to be wired. Without it the rules are emitted, a method registers them, and nothing calls
///     that method — which is how every <c>[RollUp]</c> in a generated application came to maintain
///     nothing.
/// </remarks>
internal sealed class RollUpMetadataTemplate : CSharpTemplate
{
    private readonly string _assemblyName;

    public RollUpMetadataTemplate(string assemblyName) => _assemblyName = assemblyName;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("RollUp"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        var registrationFqn = JsonEscape(GeneratedRegistrationNames.RollUpRulesFqn(_assemblyName));

        AppendLine("[assembly: PragmaticMetadata(MetadataCategory.RollUpRules, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Persistence\",");
        AppendLine($"\"registrationMethod\": \"{registrationFqn}\"");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
