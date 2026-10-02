using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates the concrete Tag entity class: <c>{Boundary}Tag : TagBase</c>.
/// One Tag entity per boundary — shared across all [HasTags] entities in the same boundary.
/// </summary>
internal sealed class TagEntityTemplate : CSharpTemplate
{
    private readonly TagTraitModel _model;

    public TagEntityTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TagTypeName, "Entity", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Tags");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated shared Tag entity for the {_model.BoundaryName ?? _model.ParentTypeName} boundary.");
        Class(_model.TagTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, Sealed = true },
            baseType: "TagBase");
    }

    private void RenderBody()
    {
        // Tag entity is mostly defined in TagBase — generated entity adds navigations to junction tables
    }
}
