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

        string Slug(string operation)
            => TraitPermissions.Slug(_model.BoundaryName, _model.ParentTypeName, TraitPermissions.CommentsGroup, operation);

        XmlSummary($"SG-generated permission constants for comments on <see cref=\"{_model.ParentTypeName}\"/>.");
        Class($"{_model.ParentTypeName}CommentPermissions", () =>
        {
            AppendLine($"public const string Create = \"{Slug(TraitPermissions.Create)}\";");
            AppendLine($"public const string Read = \"{Slug(TraitPermissions.Read)}\";");

            // No Update action/endpoint is generated when editing is off — emitting the constant
            // would advertise a grantable permission that gates nothing.
            if (_model.AllowEditing)
                AppendLine($"public const string Update = \"{Slug(TraitPermissions.Update)}\";");

            AppendLine($"public const string Delete = \"{Slug(TraitPermissions.Delete)}\";");

            // Always emitted: besides gating the moderation endpoint (RequireApproval only), this is
            // what lets someone edit or delete a comment they did not write.
            AppendLine($"public const string Moderate = \"{Slug(TraitPermissions.Moderate)}\";");

            // Who reads and writes comments marked Internal. Without it the visibility was a label:
            // anyone who could read the thread read the staff's notes, and anyone who could add a
            // comment could mark it internal.
            if (_model.SupportInternalNotes)
                AppendLine($"public const string ViewInternal = \"{Slug(TraitPermissions.ViewInternal)}\";");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { IsStatic = true });
    }
}
