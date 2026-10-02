using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     The <c>IRole</c> members of a <c>[Role]</c> class: <c>Name</c>, <c>Description</c> and the flattened
///     <c>DefaultPermissions</c> — the same list the role registry carries.
/// </summary>
internal sealed class RoleMembersTemplate : CSharpTemplate
{
    private readonly DeclaredRoleModel _role;
    private readonly ImmutableArray<string> _permissions;

    public RoleMembersTemplate(DeclaredRoleModel role, ImmutableArray<string> permissions)
    {
        _role = role;
        _permissions = permissions;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_role.TypeName, "Role", _role.Namespace),
        ToSourceText());

    protected override bool Validate() => _role.CanBeGenerated && !string.IsNullOrEmpty(_role.Name);

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_role.Namespace))
        {
            AppendNamespace(_role.Namespace);
            AppendLine();
        }

        Class(_role.TypeName, () =>
        {
            XmlSummary("The role's name — what a claim carries.");
            ExpressionProperty("Name", "string", $"\"{StringHelper.CSharpLiteral(_role.Name)}\"", isStatic: true);
            AppendLine();

            XmlSummary("What the role is for.");
            ExpressionProperty("Description", "string?",
                _role.Description is null ? "null" : $"\"{StringHelper.CSharpLiteral(_role.Description)}\"",
                isStatic: true);
            AppendLine();

            XmlSummary("Every permission the role grants — its own and its included roles', de-duplicated and ordered.");
            var values = string.Join(", ", _permissions.Select(p => $"\"{StringHelper.CSharpLiteral(p)}\""));
            AppendLine($"public static global::System.Collections.Generic.IReadOnlyList<string> DefaultPermissions {{ get; }} = [{values}];");
        },
        interfaces: ["global::Pragmatic.Authorization.IRole"],
        accessModifier: AccessModifier.NotApplicable,
        modifiers: new ClassModifiers { Partial = true });
    }
}
