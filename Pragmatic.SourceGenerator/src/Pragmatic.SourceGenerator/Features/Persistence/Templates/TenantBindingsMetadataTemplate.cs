using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits <c>_Metadata.TenantBindings.g.cs</c> — how an assembly says one of its operations takes the
///     request's tenant.
/// </summary>
/// <remarks>
///     A count, as <c>ClockBindingsMetadataTemplate</c>'s is: the host names none of these operations, it
///     decides whether it can resolve a tenant at all and says so if it cannot (<c>PRAG1615</c>).
/// </remarks>
internal sealed class TenantBindingsMetadataTemplate : CSharpTemplate
{
    private readonly int _declarations;

    public TenantBindingsMetadataTemplate(int declarations) => _declarations = declarations;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/MultiTenancy";

    protected override string? TriggerInfo => "[FromTenant] on an operation's property";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload(int declarations)
        => $$"""
            {
              "generator": "Pragmatic.SourceGenerator/MultiTenancy",
              "declarationsCount": {{declarations}}
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("TenantBindings"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)34 /* TenantBindings */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/MultiTenancy\",");
        AppendLine($"\"declarationsCount\": {_declarations}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
