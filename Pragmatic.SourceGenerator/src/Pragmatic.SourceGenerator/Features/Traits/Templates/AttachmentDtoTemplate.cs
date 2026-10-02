using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class AttachmentDtoTemplate : TraitDtoTemplateBase
{
    private readonly AttachmentTraitModel _model;
    public AttachmentDtoTemplate(AttachmentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasAttachments] DTO for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType($"{_model.AttachmentTypeName}Dto", "Dto", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Linq.Expressions");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        var dtoName = $"{_model.AttachmentTypeName}Dto";
        XmlSummary($"SG-generated DTO for <see cref=\"{_model.AttachmentTypeName}\"/>.");
        Class(dtoName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        var shape = TraitDtoShape.Attachment(_model);
        RenderDtoProperties(shape, useRequired: false);
        AppendLine();
        RenderProjection(shape, _model.AttachmentTypeName, $"{_model.AttachmentTypeName}Dto", expressionBodied: false);
    }
}
