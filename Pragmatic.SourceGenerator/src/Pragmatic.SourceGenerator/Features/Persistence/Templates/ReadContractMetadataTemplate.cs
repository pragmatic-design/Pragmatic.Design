using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The assembly attributes that tell a host which read contracts this module publishes, and what
///     to call to bind them.
/// </summary>
/// <remarks>
///     <para>
///         The host reads metadata off its <b>references</b> only, so a module has to publish this for
///         its contract to be registered. ⚠️ Without it the interface, the implementation and
///         <c>Add{Module}Reads()</c> were all generated and nothing called the last one: an action
///         injecting the contract failed at resolution, on the <b>first request</b> rather than at
///         startup. It is the same shape as the lookup caches, one floor down.
///     </para>
///     <para>
///         One attribute per contract, because an assembly can publish several — one per boundary —
///         and the metadata attribute allows multiples. A single document listing them all would have
///         to invent a shape for "many"; the reader already handles many entries in one category.
///     </para>
/// </remarks>
internal sealed class ReadContractMetadataTemplate : CSharpTemplate
{
    private readonly IReadOnlyList<string> _registrationMethods;

    public ReadContractMetadataTemplate(IReadOnlyList<string> registrationMethods)
        => _registrationMethods = registrationMethods;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("ReadContracts"),
        ToSourceText());

    protected override bool Validate() => _registrationMethods.Count > 0;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        foreach (var method in _registrationMethods)
        {
            AppendLine("[assembly: PragmaticMetadata(MetadataCategory.ReadContracts, \"1.0\", \"\"\"");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Persistence\",");
            AppendLine($"\"registrationMethod\": \"{JsonEscape(method)}\"");
            DecreaseIndent();
            AppendLine("}");
            AppendLine("\"\"\")]");
        }
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
