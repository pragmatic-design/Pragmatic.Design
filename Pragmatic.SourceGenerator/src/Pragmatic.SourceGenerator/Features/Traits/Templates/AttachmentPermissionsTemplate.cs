using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class AttachmentPermissionsTemplate : CSharpTemplate
{
    private readonly AttachmentTraitModel _model;
    public AttachmentPermissionsTemplate(AttachmentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasAttachments] permissions for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ParentTypeName, "AttachmentPermissions", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();
        string Slug(string operation)
            => TraitPermissions.Slug(_model.BoundaryName, _model.ParentTypeName, TraitPermissions.AttachmentsGroup, operation);

        XmlSummary($"SG-generated permission constants for attachments on <see cref=\"{_model.ParentTypeName}\"/>.");
        Class($"{_model.ParentTypeName}AttachmentPermissions", () =>
        {
            AppendLine($"public const string Upload = \"{Slug(TraitPermissions.Upload)}\";");
            AppendLine($"public const string Read = \"{Slug(TraitPermissions.Read)}\";");
            AppendLine($"public const string Delete = \"{Slug(TraitPermissions.Delete)}\";");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { IsStatic = true });
    }
}
