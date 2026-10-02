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

        string Slug(string operation)
            => TraitPermissions.Slug(_model.BoundaryName, _model.ParentTypeName, TraitPermissions.NotesGroup, operation);

        XmlSummary($"SG-generated permission constants for notes on <see cref=\"{_model.ParentTypeName}\"/>.");
        Class($"{_model.ParentTypeName}NotePermissions", () =>
        {
            AppendLine($"public const string Create = \"{Slug(TraitPermissions.Create)}\";");
            AppendLine($"public const string Read = \"{Slug(TraitPermissions.Read)}\";");

            // No Update action/endpoint is generated when editing is off — emitting the constant
            // would advertise a grantable permission that gates nothing.
            if (_model.AllowEditing)
                AppendLine($"public const string Update = \"{Slug(TraitPermissions.Update)}\";");

            AppendLine($"public const string Delete = \"{Slug(TraitPermissions.Delete)}\";");
            // Lets a moderator edit or delete a note they did not write.
            AppendLine($"public const string Moderate = \"{Slug(TraitPermissions.Moderate)}\";");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { IsStatic = true });
    }
}
