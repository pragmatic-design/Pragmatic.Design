using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     Generates the read-side DTO for [HasTags]: <c>{Parent}TagDto</c>.
///     Projected from the junction, which is the only place holding both the link audit and a
///     path to the tag itself — hence the <c>Tag</c> navigation in the projection.
/// </summary>
internal sealed class TagDtoTemplate : TraitDtoTemplateBase
{
    private readonly TagTraitModel _model;
    public TagDtoTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] DTO for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TagDtoTypeName, "Dto", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Linq.Expressions");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated DTO for a tag applied to a <see cref=\"{_model.ParentTypeName}\"/>.");
        Class(_model.TagDtoTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        var shape = TraitDtoShape.Tag(_model);
        RenderDtoProperties(shape, useRequired: false);
        AppendLine();
        RenderProjection(shape, _model.JunctionTypeName, _model.TagDtoTypeName, expressionBodied: false);
    }
}
