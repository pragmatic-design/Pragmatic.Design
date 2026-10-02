using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class NoteEntityTemplate : CSharpTemplate
{
    private readonly NoteTraitModel _model;
    public NoteEntityTemplate(NoteTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasNotes] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.NoteTypeName, "Entity", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Notes");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();
        XmlSummary($"SG-generated note entity for <see cref=\"{_model.ParentTypeName}\"/>.");
        Class(_model.NoteTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, Sealed = true },
            baseType: $"NoteBase<{_model.SimpleIdType}>");
    }

    private void RenderBody()
    {
        XmlSummary($"FK to the parent <see cref=\"{_model.ParentTypeName}\"/>.");
        AppendLine($"public {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; set; }}");
        AppendLine();
        XmlSummary("Navigation to the parent entity.");
        AppendLine($"public {_model.ParentTypeName}? {_model.ParentTypeName} {{ get; set; }}");
    }
}
