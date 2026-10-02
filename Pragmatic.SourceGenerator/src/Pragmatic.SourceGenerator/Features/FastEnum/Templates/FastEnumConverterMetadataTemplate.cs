using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.FastEnum.Templates;

/// <summary>
///     Emits <c>_Metadata.FastEnumConverters.g.cs</c> — the assembly attribute through which the
///     Composition host discovers this assembly's converter registration and calls it while
///     configuring the HTTP JSON options.
/// </summary>
internal sealed class FastEnumConverterMetadataTemplate : CSharpTemplate
{
    private readonly string _namespace;

    public FastEnumConverterMetadataTemplate(string ns) => _namespace = ns;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/FastEnum";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("FastEnumConverters"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        var registrationFqn = JsonEscape(GeneratedRegistrationNames.FastEnumConvertersFqn(_namespace));

        AppendLine($"[assembly: PragmaticMetadata((MetadataCategory){MetadataCategoryIds.FastEnumConverters} /* FastEnumConverters */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/FastEnum\",");
        AppendLine($"\"registrationMethod\": \"{registrationFqn}\"");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
