using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates the M:N junction entity: <c>{Parent}Tag : EntityTagBase&lt;TId&gt;</c>.
/// Composite PK: (ParentEntityId, TagId). Has typed FK to both parent and Tag entities.
/// </summary>
internal sealed class TagJunctionTemplate : CSharpTemplate
{
    private readonly TagTraitModel _model;

    public TagJunctionTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.JunctionTypeName, "Entity", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Tags");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated M:N junction between <see cref=\"{_model.ParentTypeName}\"/> and <see cref=\"{_model.TagTypeName}\"/>.");
        Class(_model.JunctionTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, Sealed = true },
            baseType: $"EntityTagBase<{_model.SimpleIdType}>");
    }

    private void RenderBody()
    {
        XmlSummary($"FK to the parent <see cref=\"{_model.ParentTypeName}\"/>.");
        AppendLine($"public {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; set; }}");
        AppendLine();

        XmlSummary("Navigation to the parent entity.");
        AppendLine($"public {_model.ParentTypeName}? {_model.ParentTypeName} {{ get; set; }}");
        AppendLine();

        XmlSummary("Navigation to the Tag entity.");
        AppendLine($"public {_model.TagTypeName}? Tag {{ get; set; }}");
    }
}
