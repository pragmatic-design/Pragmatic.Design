using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates a partial class with virtual method forwarding stubs for a decorator.
///     The developer overrides only the methods they want to intercept — all others
///     delegate to the inner service automatically.
/// </summary>
internal sealed class DecoratorDelegationTemplate : CSharpTemplate
{
    private readonly DecoratorModel _model;

    public DecoratorDelegationTemplate(DecoratorModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";
    protected override string? SourceInfo => $"{_model.TypeName} decorating {_model.DecoratedInterface}";
    protected override string? TriggerInfo => $"[Decorator] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            $"{_model.TypeName}.Decorator.g.cs",
            ToSourceText());
    }

    protected override bool Validate()
        => _model.HasInnerServiceParameter && _model.InterfaceMethods.Length > 0;

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Auto-generated delegation stubs for <see cref=\"{_model.TypeName}\"/>. Override methods to add cross-cutting behavior.");

        AppendLine($"{_model.Accessibility} partial class {_model.TypeName}");
        AppendLine("{");
        IncreaseIndent();

        // Generate the _inner field
        AppendLine($"private readonly {_model.DecoratedInterface} _inner;");
        AppendLine();

        // Generate virtual forwarding methods
        for (var i = 0; i < _model.InterfaceMethods.Length; i++)
        {
            var method = _model.InterfaceMethods[i];
            RenderForwardingMethod(method);
            if (i < _model.InterfaceMethods.Length - 1)
                AppendLine();
        }

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderForwardingMethod(InterfaceMethodModel method)
    {
        XmlSummary($"Forwards to inner service. Override to add cross-cutting behavior.");

        var prefix = method.IsAsync ? "virtual async " : "virtual ";
        var call = $"_inner.{method.Name}({method.ParameterNames})";
        var body = method.IsAsync && !method.IsVoid
            ? $"await {call}.ConfigureAwait(false)"
            : method.IsAsync && method.IsVoid
                ? $"await {call}.ConfigureAwait(false)"
                : call;

        if (method.IsVoid && !method.IsAsync)
        {
            AppendLine($"public {prefix}{method.ReturnType} {method.Name}({method.Parameters})");
            Block(() => AppendLine($"{call};"));
        }
        else if (method.IsAsync)
        {
            AppendLine($"public {prefix}{method.ReturnType} {method.Name}({method.Parameters})");
            Block(() =>
            {
                if (method.IsVoid)
                    AppendLine($"{body};");
                else
                    AppendLine($"return {body};");
            });
        }
        else
        {
            AppendLine(
                $"public {prefix}{method.ReturnType} {method.Name}({method.Parameters}) => {call};");
        }
    }
}
