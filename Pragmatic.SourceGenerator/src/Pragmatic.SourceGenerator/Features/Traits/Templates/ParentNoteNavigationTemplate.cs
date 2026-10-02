using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class ParentNoteNavigationTemplate : CSharpTemplate
{
    private readonly NoteTraitModel _model;
    public ParentNoteNavigationTemplate(NoteTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasNotes] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ParentTypeName, "NoteNavigation", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();
        XmlSummary($"SG-generated note navigation for <see cref=\"{_model.ParentTypeName}\"/>.");
        Class(_model.ParentTypeName, () =>
        {
            XmlSummary("Internal staff notes on this entity.");
            AppendLine($"public ICollection<{_model.NoteTypeName}> Notes {{ get; set; }} = new List<{_model.NoteTypeName}>();");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Partial = true });
    }
}
