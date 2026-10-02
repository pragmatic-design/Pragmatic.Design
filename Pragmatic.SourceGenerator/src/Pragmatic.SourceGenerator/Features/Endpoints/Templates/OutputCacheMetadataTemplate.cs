using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Emits <c>_Metadata.OutputCache.g.cs</c>: this assembly declares a response the server may keep for
///     any caller, so its host needs the output cache.
/// </summary>
/// <remarks>Presence and nothing else, as the feature-flag and wire-type documents are.</remarks>
internal sealed class OutputCacheMetadataTemplate : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";

    protected override string? TriggerInfo => "[ResponseCache] with a shared location";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload()
        => """
            {
              "generator": "Pragmatic.SourceGenerator/Endpoints"
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("OutputCache"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata(MetadataCategory.OutputCache, \"1.0\", \"\"\"");
        AppendLine(Payload());
        AppendLine("\"\"\")]");
    }
}
