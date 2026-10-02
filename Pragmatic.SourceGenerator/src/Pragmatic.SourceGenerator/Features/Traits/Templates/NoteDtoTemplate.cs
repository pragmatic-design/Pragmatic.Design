using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class NoteDtoTemplate : TraitDtoTemplateBase
{
    private readonly NoteTraitModel _model;
    public NoteDtoTemplate(NoteTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasNotes] DTO for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType($"{_model.NoteTypeName}Dto", "Dto", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Linq.Expressions");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        var dtoName = $"{_model.NoteTypeName}Dto";
        XmlSummary($"SG-generated DTO for <see cref=\"{_model.NoteTypeName}\"/>.");
        Class(dtoName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        var shape = TraitDtoShape.Note(_model);
        RenderDtoProperties(shape, useRequired: false);
        AppendLine();
        RenderProjection(shape, _model.NoteTypeName, $"{_model.NoteTypeName}Dto", expressionBodied: false);
    }
}
