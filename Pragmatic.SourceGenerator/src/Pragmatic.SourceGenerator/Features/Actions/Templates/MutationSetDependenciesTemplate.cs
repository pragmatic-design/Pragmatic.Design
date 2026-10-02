using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed class MutationSetDependenciesTemplate : CSharpTemplate
{
    private readonly MutationModel _model;

    public MutationSetDependenciesTemplate(MutationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} Mutation from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Mutation] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "SetDependencies", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, HasDependencies: true };

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
        XmlSummary("Sets the dependencies for this mutation.");

        var parameters = _model.Dependencies
            .Select(d => new MethodParameter(d.TypeName, TemplateHelpers.ToCamelCase(StripUnderscore(d.FieldName))))
            .ToList();

        Method("SetDependencies", RenderMethodBody, "void", parameters, AccessModifier.Internal);
    }

    private void RenderMethodBody()
    {
        foreach (var dep in _model.Dependencies)
        {
            var paramName = TemplateHelpers.ToCamelCase(StripUnderscore(dep.FieldName));
            AppendLine($"{dep.FieldName} = {paramName};");
        }
    }

    private static string StripUnderscore(string fieldName)
        => fieldName.StartsWith("_") ? fieldName.Substring(1) : fieldName;
}
