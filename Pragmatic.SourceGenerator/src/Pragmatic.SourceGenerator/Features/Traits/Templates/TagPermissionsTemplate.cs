using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates permission constants for tag operations on an entity.
/// </summary>
internal sealed class TagPermissionsTemplate : CSharpTemplate
{
    private readonly TagTraitModel _model;

    public TagPermissionsTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ParentTypeName, "TagPermissions", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"Permission constants for tag operations on <see cref=\"{_model.ParentTypeName}\"/>.");
        Class($"{_model.ParentTypeName}TagPermissions", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        string Slug(string operation)
            => TraitPermissions.Slug(_model.BoundaryName, _model.ParentTypeName, TraitPermissions.TagsGroup, operation);

        AppendLine($"public const string Add = \"{Slug(TraitPermissions.Add)}\";");
        AppendLine($"public const string Remove = \"{Slug(TraitPermissions.Remove)}\";");
        AppendLine($"public const string Read = \"{Slug(TraitPermissions.Read)}\";");
    }
}
