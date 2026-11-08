using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates permission constants for comment operations on a parent entity.
/// </summary>
internal sealed class CommentPermissionsTemplate : CSharpTemplate
{
    private readonly CommentTraitModel _model;

    public CommentPermissionsTemplate(CommentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasComments] permissions for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ParentTypeName, "CommentPermissions", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated permission constants for comments on <see cref=\"{_model.ParentTypeName}\"/>.");
        Class($"{_model.ParentTypeName}CommentPermissions", () =>
        {
            foreach (var (member, value) in TraitPermissionMembers.Comment(_model))
                AppendLine($"public const string {member} = \"{value}\";");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { IsStatic = true });
    }
}
