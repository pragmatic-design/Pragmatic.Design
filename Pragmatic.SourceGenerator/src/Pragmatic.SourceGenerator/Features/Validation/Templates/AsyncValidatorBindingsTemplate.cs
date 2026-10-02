using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>
///     Template for generating IAsyncValidatorBindings&lt;T&gt; implementation
///     from [AsyncValidate&lt;T&gt;] attribute bindings.
/// </summary>
internal sealed class AsyncValidatorBindingsTemplate : CSharpTemplate
{
    private readonly AsyncValidatorBindingsModel _model;

    public AsyncValidatorBindingsTemplate(AsyncValidatorBindingsModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Validation";
    protected override string? SourceInfo => $"{_model.TypeName} async validator bindings";
    protected override string? TriggerInfo => $"[AsyncValidate] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "AsyncValidatorBindings", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");
        AddUsing("Pragmatic.Validation");

        foreach (var binding in _model.Bindings)
        {
            var ns = GetNamespace(binding.ValidatorFullTypeName);
            if (!string.IsNullOrEmpty(ns))
                AddUsing(ns);
        }

        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AddUsing(_model.Namespace);
            AppendNamespace(_model.Namespace);
        }

        AppendLine();

        var interfaces = new List<string> { $"IAsyncValidatorBindings<{_model.TypeName}>" };

        Class(NamingHelper.AppendSuffix(_model.TypeName, "AsyncValidatorBindings"), RenderBody,
            interfaces: interfaces,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        XmlSummary($"Determines whether the given async validator should be invoked for {_model.TypeName}.");
        var parameters = new List<MethodParameter>
        {
            new("Type", "validatorType"),
            new("IReadOnlySet<string>?", "modifiedProperties")
        };

        Method("ShouldInvoke", RenderShouldInvokeBody, "bool", parameters);
    }

    private void RenderShouldInvokeBody()
    {
        // Create mode: all bound validators invoked
        If("modifiedProperties is null", () => Return("true"));
        AppendLine();

        // Entity-level bindings (always invoked)
        var entityLevelBindings = _model.Bindings
            .Where(b => b.TriggerPropertyName is null)
            .ToList();

        foreach (var binding in entityLevelBindings)
        {
            var shortName = GetShortTypeName(binding.ValidatorFullTypeName);
            Comment("Entity-level: always invoke");
            If($"validatorType == typeof({shortName})", () => Return("true"));
            AppendLine();
        }

        // Property-level bindings (invoke only when trigger property modified)
        var propertyLevelBindings = _model.Bindings
            .Where(b => b.TriggerPropertyName is not null)
            .ToList();

        foreach (var binding in propertyLevelBindings)
        {
            var shortName = GetShortTypeName(binding.ValidatorFullTypeName);
            Comment($"Property-level: fires when {binding.TriggerPropertyName} changes");
            If($"validatorType == typeof({shortName})",
                () => Return($"modifiedProperties.Contains(\"{binding.TriggerPropertyName}\")"));
            AppendLine();
        }

        Return("false");
    }

    private static string GetNamespace(string fullTypeName)
    {
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot > 0 ? fullTypeName.Substring(0, lastDot) : "";
    }

    private static string GetShortTypeName(string fullTypeName)
    {
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot > 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
    }
}
