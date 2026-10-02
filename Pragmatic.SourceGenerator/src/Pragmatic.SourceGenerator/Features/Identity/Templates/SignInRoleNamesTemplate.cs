using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     <c>{Enum}RoleNames.RoleNameOf(this {Enum})</c>: the role each member of a <c>[SignsInAs]</c> enum signs in as, as a
///     switch over every member.
/// </summary>
/// <remarks>
///     Each arm answers <c>TRole.Name</c> — the role's own static member, which this generator writes for a
///     <c>[Role]</c> class — so the name is written once, on the role. The default arm is for a value no member names
///     (a cast integer): every declared member has its arm, PRAG1014 otherwise, and nothing is generated then.
/// </remarks>
internal sealed class SignInRoleNamesTemplate : CSharpTemplate
{
    private readonly SignInRoleMapModel _model;

    public SignInRoleNamesTemplate(SignInRoleMapModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    private string ClassName => NamingHelper.AppendSuffix(_model.EnumName, "RoleNames");

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(ClassName, "SignIn", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.Unmapped.Count == 0 && _model.Arms.Count > 0;

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"The role a <see cref=\"{_model.EnumFullName}\" /> signs in as — one arm per member, from its [SignsInAs].");
        Class(ClassName, () =>
        {
            XmlSummary("The name of the role <paramref name=\"value\" /> signs in as — what the sign-in puts in the token.");
            AppendLine($"public static string RoleNameOf(this {_model.EnumFullName} value) => value switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var arm in _model.Arms)
                AppendLine($"{_model.EnumFullName}.{arm.Member} => {arm.RoleFullName}.Name,");
            AppendLine($"_ => throw new global::System.ArgumentOutOfRangeException(nameof(value), value, \"A value no member of {StringHelper.CSharpLiteral(_model.EnumName)} declares.\")");
            DecreaseIndent();
            AppendLine("};");
        },
        accessModifier: _model.IsPublic ? AccessModifier.Public : AccessModifier.Internal,
        modifiers: new ClassModifiers { IsStatic = true });
    }
}
