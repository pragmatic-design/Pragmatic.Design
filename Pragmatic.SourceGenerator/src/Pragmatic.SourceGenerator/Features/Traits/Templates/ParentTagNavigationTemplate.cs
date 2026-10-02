using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates the navigation property <c>ICollection&lt;{Parent}Tag&gt; Tags</c> on the parent entity.
/// </summary>
internal sealed class ParentTagNavigationTemplate : CSharpTemplate
{
    private readonly TagTraitModel _model;

    public ParentTagNavigationTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ParentTypeName, "TagNavigation", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated tag navigation for <see cref=\"{_model.ParentTypeName}\"/>.");
        Class(_model.ParentTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        XmlSummary("Tags applied to this entity (M:N via junction table).");
        AppendLine($"public ICollection<{_model.JunctionTypeName}> Tags {{ get; set; }} = new List<{_model.JunctionTypeName}>();");
    }
}
