using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates the SetDependencies method for endpoints with private field dependencies.
/// </summary>
internal sealed class SetDependenciesTemplate : CSharpTemplate
{
    private readonly EndpointModel _model;

    public SetDependenciesTemplate(EndpointModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Endpoint] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            _model.ResolveHintName("SetDependencies"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, HasDependencies: true };
    }

    public override void RenderFile()
    {
        AddUsing("System.Runtime.CompilerServices");

        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderClassBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        XmlSummary("Sets the dependencies for this endpoint.");
        foreach (var dep in _model.Dependencies)
            XmlParam(ToCamelCase(GetFieldNameWithoutUnderscore(dep.FieldName)),
                $"The {dep.TypeName.Split('.').Last()} dependency.");

        var parameters = _model.Dependencies
            .Select(d => new MethodParameter(d.TypeName, ToCamelCase(GetFieldNameWithoutUnderscore(d.FieldName))))
            .ToList();

        Method("SetDependencies", RenderMethodBody, "void", parameters,
            AccessModifier.Internal,
            new MethodModifiers { IsStatic = false });
    }

    private void RenderMethodBody()
    {
        foreach (var dep in _model.Dependencies)
        {
            var paramName = ToCamelCase(GetFieldNameWithoutUnderscore(dep.FieldName));
            AppendLine($"{dep.FieldName} = {paramName};");
        }
    }

    private static string GetFieldNameWithoutUnderscore(string fieldName)
    {
        if (fieldName.StartsWith("_"))
            return fieldName.Substring(1);
        return fieldName;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}
