using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration.Templates;

/// <summary>
/// Generates the IServiceCollection extension method that binds, validates, and registers an options class.
/// </summary>
internal sealed class RegistrationTemplate : CSharpTemplate
{
    private readonly ConfigurationModel _model;
    private readonly string? _validatorType;

    public RegistrationTemplate(ConfigurationModel model, string? validatorType = null)
    {
        _model = model;
        _validatorType = validatorType;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Configuration";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Configuration] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Registration", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsPartial;

    public override void RenderFile()
    {
        AddUsings(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"Registers <see cref=\"{_model.TypeName}\"/> bound to configuration section \"{_model.SectionPath}\".");

        Class(
            NamingHelper.AppendSuffix(_model.TypeName, "ConfigurationExtensions"),
            RenderBody,
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderBody()
    {
        var extensionsMethodName = $"Add{NamingHelper.AppendSuffix(_model.TypeName, "Options")}";
        var returnType = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
        var parameters = new List<MethodParameter>
        {
            new("this " + returnType, "services"),
            new("global::Microsoft.Extensions.Configuration.IConfiguration", "configuration")
        };

        Method(extensionsMethodName, RenderMethodBody, returnType, parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        AppendLine($"services.AddOptions<global::{_model.FullTypeName}>()");
        IncreaseIndent();
        AppendLine($".Bind(configuration.GetSection(\"{_model.SectionPath}\"))");

        if (_model.HasValidation)
            AppendLine(".ValidateDataAnnotations()");

        if (_model.ValidateOnStart)
            AppendLine(".ValidateOnStart();");
        else
            // Remove trailing newline from last chained call and add semicolon
            AppendLine(";");

        DecreaseIndent();

        if (_validatorType is { Length: > 0 } validator)
        {
            AppendLine();
            // Register the generated [ConfigInvariant] validator alongside DataAnnotations.
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton" +
                $"<global::Microsoft.Extensions.Options.IValidateOptions<global::{_model.FullTypeName}>, {validator}>(services);");
        }

        AppendLine();
        AppendLine("return services;");
    }
}
