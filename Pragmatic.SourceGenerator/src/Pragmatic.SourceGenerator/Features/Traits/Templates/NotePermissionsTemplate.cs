using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class NotePermissionsTemplate : CSharpTemplate
{
    private readonly NoteTraitModel _model;
    public NotePermissionsTemplate(NoteTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasNotes] permissions for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ParentTypeName, "NotePermissions", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated permission constants for notes on <see cref=\"{_model.ParentTypeName}\"/>.");
        Class($"{_model.ParentTypeName}NotePermissions", () =>
        {
            foreach (var (member, value) in TraitPermissionMembers.Note(_model))
                AppendLine($"public const string {member} = \"{value}\";");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { IsStatic = true });
    }
}
