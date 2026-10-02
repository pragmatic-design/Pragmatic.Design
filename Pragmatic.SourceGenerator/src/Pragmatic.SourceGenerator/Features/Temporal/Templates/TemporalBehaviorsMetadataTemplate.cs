using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Temporal.Templates;

/// <summary>
///     Emits <c>_Metadata.TemporalBehaviors.g.cs</c> — assembly metadata so the Composition host
///     aggregates and calls the generated behaviors registration. Category 21 = TemporalBehaviors
///     (cast, as the runtime enum stops at Sagas=17 — same pattern as JsonContexts=20).
/// </summary>
internal sealed class TemporalBehaviorsMetadataTemplate : CSharpTemplate
{
    private readonly string _registrationMethodFqn;
    private readonly int _behaviorsCount;

    public TemporalBehaviorsMetadataTemplate(string registrationMethodFqn, int behaviorsCount)
    {
        _registrationMethodFqn = registrationMethodFqn;
        _behaviorsCount = behaviorsCount;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Temporal";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("TemporalBehaviors"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)21 /* TemporalBehaviors */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Temporal\",");
        AppendLine($"\"registrationMethod\": \"{JsonEscape(_registrationMethodFqn)}\",");
        AppendLine($"\"data\": {{ \"behaviorsCount\": {_behaviorsCount} }}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
