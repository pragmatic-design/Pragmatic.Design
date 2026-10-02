using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Emits the <c>[assembly: PragmaticMetadata]</c> that tells a host to call this assembly's
///     <c>AddGeneratedActionPermissionCatalog</c>. Category Authorization, the same one Identity uses:
///     the host calls every <i>distinct</i> registration method it finds under that category, so the two
///     coexist and both catalogs reach <c>DefaultPermissionCatalog</c>.
/// </summary>
/// <remarks>
///     A separate hint name from Identity's metadata is not cosmetic — two outputs sharing one hint make
///     Roslyn discard the generator's <i>entire</i> output with a CS8785 that is only a warning.
/// </remarks>
internal sealed class ActionPermissionCatalogMetadataTemplate : CSharpTemplate
{
    private readonly int _permissionCount;
    private readonly string _registrationMethodFqn;

    public ActionPermissionCatalogMetadataTemplate(string registrationMethodFqn, int permissionCount)
    {
        _registrationMethodFqn = registrationMethodFqn;
        _permissionCount = permissionCount;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("ActionPermissions"),
        ToSourceText());

    protected override bool Validate() => _permissionCount > 0;

    public override void RenderFile()
    {
        AppendLine();
        // MetadataCategory 22 = Authorization catalog; the runtime enum stops at 17, so cast numerically
        // exactly as AuthorizationMetadataTemplate does.
        AppendLine(
            $"[assembly: global::Pragmatic.Composition.Attributes.PragmaticMetadataAttribute((global::Pragmatic.Composition.Metadata.MetadataCategory){MetadataCategoryIds.Authorization}, \"{MetadataSchemaVersions.Authorization}\", \"\"\"");
        AppendLine(BuildJson());
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(indent: false);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Actions.SourceGenerator");
        builder.Property("registrationMethod", _registrationMethodFqn);

        builder.Property("data");
        builder.StartObject();
        builder.Property("permissionsCount", _permissionCount);
        builder.Property("rolesCount", 0);
        builder.EndObject();

        builder.EndObject();

        return builder.ToString();
    }
}
