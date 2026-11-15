using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits <c>_Metadata.TenantSource.g.cs</c> — how an assembly says its <c>[TenantSource]</c> entity's rows are the tenants, naming the
///     factory of the store the generator wrote over it. The host registers that store in place of the empty in-memory one.
/// </summary>
internal sealed class TenantSourceMetadataTemplate : CSharpTemplate
{
    private readonly string _entity;
    private readonly string _factory;

    public TenantSourceMetadataTemplate(string entity, string factory)
    {
        _entity = entity;
        _factory = factory;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/MultiTenancy";

    protected override string? TriggerInfo => $"[TenantSource] on {_entity}";

    /// <summary>The document, shared with the host-local entry so the two cannot drift.</summary>
    public static string Payload(string entity, string factory)
        => $$"""
            {
              "generator": "Pragmatic.SourceGenerator/MultiTenancy",
              "entity": "{{entity}}",
              "registrationMethod": "{{factory}}"
            }
            """;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("TenantSource"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata((MetadataCategory)36 /* TenantSource */, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/MultiTenancy\",");
        AppendLine($"\"entity\": \"{_entity}\",");
        AppendLine($"\"registrationMethod\": \"{_factory}\"");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
