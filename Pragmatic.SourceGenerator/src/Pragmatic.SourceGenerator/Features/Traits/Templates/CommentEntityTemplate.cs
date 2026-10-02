using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates the concrete comment entity class: <c>{Parent}Comment : CommentBase&lt;TId&gt;</c>.
/// </summary>
internal sealed class CommentEntityTemplate : CSharpTemplate
{
    private readonly CommentTraitModel _model;

    public CommentEntityTemplate(CommentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasComments] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.CommentTypeName, "Entity", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Comments");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated comment entity for <see cref=\"{_model.ParentTypeName}\"/>.");
        Class(_model.CommentTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, Sealed = true },
            baseType: $"CommentBase<{_model.SimpleIdType}>");
    }

    private void RenderBody()
    {
        XmlSummary($"FK to the parent <see cref=\"{_model.ParentTypeName}\"/>.");
        AppendLine($"public {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; set; }}");
        AppendLine();

        XmlSummary("Navigation to the parent entity.");
        AppendLine($"public {_model.ParentTypeName}? {_model.ParentTypeName} {{ get; set; }}");

        if (_model.AllowReplies)
        {
            AppendLine();
            XmlSummary("Navigation to replies (child comments).");
            AppendLine($"public ICollection<{_model.CommentTypeName}> Replies {{ get; set; }} = new List<{_model.CommentTypeName}>();");
        }
    }
}
