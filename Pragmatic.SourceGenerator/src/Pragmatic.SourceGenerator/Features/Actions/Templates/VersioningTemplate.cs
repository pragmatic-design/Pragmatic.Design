using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed class VersioningTemplate : CSharpTemplate
{
    private readonly ActionModel _model;

    public VersioningTemplate(ActionModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[DomainAction] versioning on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Versioning", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, HasVersioning: true };

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderClassBody,
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        XmlSummary("Gets or sets the target version for versioned execution dispatch.");
        AppendLine("internal (int Major, int Minor) TargetVersion { get; set; } = (1, 0);");
    }
}
