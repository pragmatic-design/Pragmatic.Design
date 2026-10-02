using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.FeatureFlags.Templates;

/// <summary>
///     Emits <c>_Metadata.FeatureFlags.g.cs</c> — how an assembly says it declares a feature flag.
/// </summary>
/// <remarks>
///     A count and nothing else: the host does not name any of these types, it decides whether to
///     call <c>AddPragmaticFeatureFlags()</c>. The number is there so the file answers the question a
///     person opens it to ask — <em>who asked for this?</em> — rather than merely existing.
/// </remarks>
internal sealed class FeatureFlagsMetadataTemplate : CSharpTemplate
{
    private readonly int _declarations;

    public FeatureFlagsMetadataTemplate(int declarations) => _declarations = declarations;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/FeatureFlags";

    protected override string? TriggerInfo => "IFeatureFlag implementations";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload(int declarations)
        => $$"""
            {
              "generator": "Pragmatic.SourceGenerator/FeatureFlags",
              "declarationsCount": {{declarations}}
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("FeatureFlags"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)29 /* FeatureFlags */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/FeatureFlags\",");
        AppendLine($"\"declarationsCount\": {_declarations}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
