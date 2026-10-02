using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Emits <c>[assembly: PragmaticMetadata((MetadataCategory)22, ...)]</c> carrying the FQN of this
///     assembly's <c>AddGeneratedAuthorizationCatalog</c> registration method. The host reads this via
///     <c>MetadataReader</c> and calls the method so every referenced assembly's permission/role registry
///     flows into <c>DefaultPermissionCatalog</c>. Emitted only when Pragmatic.Composition is referenced.
/// </summary>
internal sealed class AuthorizationMetadataTemplate : CSharpTemplate
{
    private readonly int _permissionCount;
    private readonly string _registrationMethodFqn;
    private readonly int _roleCount;
    private readonly bool _indent;

    public AuthorizationMetadataTemplate(
        string registrationMethodFqn,
        int permissionCount,
        int roleCount,
        bool indent)
    {
        _registrationMethodFqn = registrationMethodFqn;
        _permissionCount = permissionCount;
        _roleCount = roleCount;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Authorization"),
        ToSourceText());

    protected override bool Validate() => _permissionCount > 0 || _roleCount > 0;

    public override void RenderFile()
    {
        var json = BuildJson();

        AppendLine();
        // MetadataCategory 22 = Authorization catalog. The runtime enum stops at 17, so cast numerically
        // (same approach as the Manifest/JsonContexts/TemporalBehaviors metadata templates).
        AppendLine(
            $"[assembly: global::Pragmatic.Composition.Attributes.PragmaticMetadataAttribute((global::Pragmatic.Composition.Metadata.MetadataCategory){MetadataCategoryIds.Authorization}, \"{MetadataSchemaVersions.Authorization}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Identity.SourceGenerator");
        builder.Property("registrationMethod", _registrationMethodFqn);

        builder.Property("data");
        builder.StartObject();
        builder.Property("permissionsCount", _permissionCount);
        builder.Property("rolesCount", _roleCount);
        builder.EndObject();

        builder.EndObject();

        return builder.ToString();
    }
}
