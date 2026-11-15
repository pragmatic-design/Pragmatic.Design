using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits <c>_Metadata.FeatureGates.g.cs</c> — how an assembly says one of its operations is refused when a
///     feature flag is off.
/// </summary>
/// <remarks>A count, as the clock's and the tenant's are: the host names none of the operations (<c>PRAG1616</c>).</remarks>
internal sealed class FeatureGatesMetadataTemplate : CSharpTemplate
{
    private readonly int _declarations;

    public FeatureGatesMetadataTemplate(int declarations) => _declarations = declarations;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/FeatureFlags";

    protected override string? TriggerInfo => "[RequiresFeature] on an operation";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload(int declarations)
        => $$"""
            {
              "generator": "Pragmatic.SourceGenerator/FeatureFlags",
              "declarationsCount": {{declarations}}
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("FeatureGates"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)35 /* FeatureGates */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/FeatureFlags\",");
        AppendLine($"\"declarationsCount\": {_declarations}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
