using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Result.Models;

namespace Pragmatic.SourceGenerator.Features.Result.Templates;

/// <summary>
///     Template for generating WriteExtensions override on partial Error implementations.
///     Emits each custom property directly — zero reflection at runtime.
/// </summary>
internal sealed class ErrorExtensionsTemplate : CSharpTemplate
{
    private readonly ErrorModel _model;

    public ErrorExtensionsTemplate(ErrorModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Result";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"WriteExtensions on {_model.TypeName}";

    protected override bool Validate() => _model.HasCustomProperties;

    public override Artifact RenderOutput()
    {
        var hintName = VirtualFolderHints.ForType(_model.TypeName, "ErrorExtensions", _model.Namespace);
        return new Artifact(hintName, ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsings("System.Collections.Generic");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        var accessibility = MapAccessibility(_model.Accessibility);
        var modifiers = new ClassModifiers { Partial = true };

        // The partial declaration MUST agree with the user's declaration on
        // class/struct/record (CS0261). Error is an abstract record, so error
        // types that extend it are records — emit `record`/`record struct`.
        switch (_model.IsRecord, _model.IsStruct)
        {
            case (true, true):
                RecordStruct(_model.TypeName, RenderBody, accessModifier: accessibility, modifiers: modifiers);
                break;
            case (true, false):
                Record(_model.TypeName, RenderBody, accessModifier: accessibility, modifiers: modifiers);
                break;
            case (false, true):
                Struct(_model.TypeName, RenderBody, accessModifier: accessibility, modifiers: modifiers);
                break;
            default:
                Class(_model.TypeName, RenderBody, accessModifier: accessibility, modifiers: modifiers);
                break;
        }
    }

    private void RenderBody()
    {
        XmlSummary("Writes custom error properties as ProblemDetails extensions (source-generated, zero reflection).");
        Method("WriteExtensions", RenderWriteExtensionsBody, "void",
            parameters:
            [
                new MethodParameter { Type = "IDictionary<string, object?>", Name = "extensions" }
            ],
            accessModifier: AccessModifier.Public,
            modifiers: new MethodModifiers { IsOverride = true });
    }

    private void RenderWriteExtensionsBody()
    {
        foreach (var prop in _model.CustomProperties)
        {
            if (prop.IsNullable || !prop.IsValueType)
            {
                // Nullable or reference type: check for null before writing
                If($"{prop.Name} is not null", () =>
                {
                    AppendLine($"extensions[\"{prop.CamelCaseName}\"] = {prop.Name};");
                });
            }
            else
            {
                // Non-nullable value type: always write
                AppendLine($"extensions[\"{prop.CamelCaseName}\"] = {prop.Name};");
            }
        }
    }

    private static AccessModifier MapAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "private" => AccessModifier.Private,
            "protected" => AccessModifier.Protected,
            _ => AccessModifier.Public
        };
    }
}
