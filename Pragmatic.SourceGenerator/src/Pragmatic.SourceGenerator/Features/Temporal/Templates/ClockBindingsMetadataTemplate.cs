using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Temporal.Templates;

/// <summary>
///     Emits <c>_Metadata.ClockBindings.g.cs</c> — how an assembly says one of its operations takes a
///     value from the clock.
/// </summary>
/// <remarks>
///     A count and nothing else, as the resilience document is: the host does not name any of these
///     operations. What it does with the fact is decide whether it can supply an <c>IClock</c> at all,
///     and say so if it cannot (<c>PRAG1698</c>). The number is there so the file answers the question
///     a person opens it to ask — <em>who asked for this?</em> — instead of merely existing.
/// </remarks>
internal sealed class ClockBindingsMetadataTemplate : CSharpTemplate
{
    private readonly int _declarations;

    public ClockBindingsMetadataTemplate(int declarations) => _declarations = declarations;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Temporal";

    protected override string? TriggerInfo => "[FromClock] on an operation's property";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload(int declarations)
        => $$"""
            {
              "generator": "Pragmatic.SourceGenerator/Temporal",
              "declarationsCount": {{declarations}}
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("ClockBindings"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)30 /* ClockBindings */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Temporal\",");
        AppendLine($"\"declarationsCount\": {_declarations}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
