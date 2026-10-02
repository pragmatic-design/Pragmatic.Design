using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Resilience.Templates;

/// <summary>
///     Emits <c>_Metadata.Resilience.g.cs</c> — how an assembly says it declares resilience.
/// </summary>
/// <remarks>
///     A count and nothing else: the host does not name any of these types, it decides whether to call
///     <c>AddPragmaticResilience()</c>. The number is there so the file answers the question a person
///     opens it to ask — <em>who asked for this?</em> — rather than merely existing.
/// </remarks>
internal sealed class ResilienceMetadataTemplate : CSharpTemplate
{
    private readonly int _declarations;

    public ResilienceMetadataTemplate(int declarations) => _declarations = declarations;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Resilience";

    protected override string? TriggerInfo =>
        "[ResiliencePolicy] / [Retry] / [Timeout] / [CircuitBreaker]";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload(int declarations)
        => $$"""
            {
              "generator": "Pragmatic.SourceGenerator/Resilience",
              "declarationsCount": {{declarations}}
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Resilience"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)28 /* Resilience */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Resilience\",");
        AppendLine($"\"declarationsCount\": {_declarations}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
