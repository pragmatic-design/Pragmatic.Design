using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class ParentAttachmentNavigationTemplate : CSharpTemplate
{
    private readonly AttachmentTraitModel _model;
    public ParentAttachmentNavigationTemplate(AttachmentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasAttachments] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ParentTypeName, "AttachmentNavigation", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();
        XmlSummary($"SG-generated attachment navigation for <see cref=\"{_model.ParentTypeName}\"/>.");
        Class(_model.ParentTypeName, () =>
        {
            XmlSummary("File attachments on this entity.");
            AppendLine($"public ICollection<{_model.AttachmentTypeName}> Attachments {{ get; set; }} = new List<{_model.AttachmentTypeName}>();");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Partial = true });
    }
}
