using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates {Parent}CommentDto — a read DTO for comment entities with a Projection expression.
/// </summary>
internal sealed class CommentDtoTemplate : TraitDtoTemplateBase
{
    private readonly CommentTraitModel _model;
    private readonly string _dtoTypeName;

    public CommentDtoTemplate(CommentTraitModel model)
    {
        _model = model;
        _dtoTypeName = $"{model.CommentTypeName}Dto";
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasComments] DTO for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_dtoTypeName, "Dto", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Comments");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated read DTO for <see cref=\"{_model.CommentTypeName}\"/>.");
        AppendLine($"public sealed record {_dtoTypeName}");
        AppendLine("{");
        IncreaseIndent();

        var shape = TraitDtoShape.Comment(_model);
        RenderDtoProperties(shape, useRequired: true);
        AppendLine();
        RenderProjection(shape, _model.CommentFullTypeName, _dtoTypeName, expressionBodied: true);

        DecreaseIndent();
        AppendLine("}");
    }
}
