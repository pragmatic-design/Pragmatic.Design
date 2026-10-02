using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration.Templates;

/// <summary>
///     Generates an <c>IValidateOptions&lt;T&gt;</c> that runs every <c>[ConfigInvariant]</c> method on the
///     options instance at startup, collecting a failure message per invariant that returns <c>false</c>.
/// </summary>
internal sealed class OptionsValidatorTemplate : CSharpTemplate
{
    private const string OptionsNamespace = "global::Microsoft.Extensions.Options";

    private readonly ConfigurationModel _model;

    public OptionsValidatorTemplate(ConfigurationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Configuration";

    /// <summary>The generated validator's simple type name.</summary>
    public string ClassName => NamingHelper.AppendSuffix(_model.TypeName, "OptionsValidator");

    /// <summary>Fully-qualified validator type for the registration call.</summary>
    public string FullyQualifiedName => _model.Namespace is { Length: > 0 } ns
        ? $"global::{ns}.{ClassName}"
        : $"global::{ClassName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "OptionsValidator", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsPartial && _model.HasInvariants;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"Runs [ConfigInvariant] checks for <see cref=\"{_model.TypeName}\"/> at startup.");

        Class(ClassName, RenderBody,
            baseType: $"{OptionsNamespace}.IValidateOptions<global::{_model.FullTypeName}>",
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        AppendLine(
            $"public {OptionsNamespace}.ValidateOptionsResult Validate(string? name, global::{_model.FullTypeName} options)");
        AppendLine("{");
        IncreaseIndent();

        AppendLine("var failures = new global::System.Collections.Generic.List<string>();");
        foreach (var invariant in _model.Invariants)
        {
            AppendLine($"if (!options.{invariant.MethodName}())");
            IncreaseIndent();
            AppendLine($"failures.Add({EscapeLiteral(invariant.Message)});");
            DecreaseIndent();
        }

        AppendLine("return failures.Count == 0");
        IncreaseIndent();
        AppendLine($"? {OptionsNamespace}.ValidateOptionsResult.Success");
        AppendLine($": {OptionsNamespace}.ValidateOptionsResult.Fail(failures);");
        DecreaseIndent();

        DecreaseIndent();
        AppendLine("}");
    }

    private static string EscapeLiteral(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
