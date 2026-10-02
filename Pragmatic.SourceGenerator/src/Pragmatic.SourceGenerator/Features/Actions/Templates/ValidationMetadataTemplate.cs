using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates compile-time validation metadata for DomainAction/Mutation classes.
///     Implements <c>IActionValidationMetadata</c> to eliminate runtime reflection
///     from ValidationFilter.
/// </summary>
internal sealed class ValidationMetadataTemplate : CSharpTemplate
{
    private readonly ActionValidationModel _model;

    public ValidationMetadataTemplate(ActionValidationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} ValidationMetadata from {_model.Namespace}";
    protected override string? TriggerInfo => $"[DomainAction] or [Mutation] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "ValidationMetadata", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Actions.Pipeline");
        AddUsing("Pragmatic.Validation");
        AddUsing("Pragmatic.Validation.Types");

        if (_model.RunAsync && !_model.AsyncNestedProperties.IsDefaultOrEmpty)
            AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderBody,
            accessModifier: _model.Accessibility == "public" ? AccessModifier.Public : AccessModifier.Internal,
            modifiers: new ClassModifiers { Partial = true },
            interfaces: new List<string> { "IActionValidationMetadata" });
    }

    private void RenderBody()
    {
        Comment("SG-generated validation metadata — implements IActionValidationMetadata");
        Comment("Eliminates all reflection from ValidationFilter");
        AppendLine();

        // HasNoValidation property
        AppendLine(
            $"bool IActionValidationMetadata.HasNoValidation => {(_model.HasNoValidation ? "true" : "false")};");

        // RunSyncValidation property
        AppendLine(
            $"bool IActionValidationMetadata.RunSyncValidation => {(_model.RunSync ? "true" : "false")};");

        // RunAsyncValidation property
        AppendLine(
            $"bool IActionValidationMetadata.RunAsyncValidation => {(_model.RunAsync ? "true" : "false")};");

        AppendLine();

        // ValidateNestedSync method
        RenderValidateNestedSync();

        AppendLine();

        // ValidateNestedAsync method
        RenderValidateNestedAsync();
    }

    private void RenderValidateNestedSync()
    {
        if (_model.SyncNestedProperties.Length > 0)
        {
            XmlSummary("Validates nested properties implementing ISyncValidator without reflection.");
            AppendLine("ValidationError? IActionValidationMetadata.ValidateNestedSync()");
            Block(() =>
            {
                AppendLine("ValidationError? combined = null;");

                // ⚠️ No `is ISyncValidator`. The property is in this list **because** the generator
                // has already established that the type will have the validator: asking again at run
                // time is a question whose answer it knows, and the negative branch would be code that
                // is never taken — indistinguishable from broken code. The call is direct because the
                // generated validator exposes `public ValidationError Validate()` on the class, not
                // only through the interface. See docs/CONVENTIONS.md, «Decide at compile time».
                foreach (var prop in _model.SyncNestedProperties)
                {
                    void Accumulate(string subject)
                    {
                        AppendLine($"var result = {subject}.Validate();");
                        AppendLine("if (result.IsFailure)");
                        IncreaseIndent();
                        AppendLine(
                            "combined = combined is null ? result : combined.Value.Combine(result);");
                        DecreaseIndent();
                    }

                    // The null check is a real question: the property may be unset.
                    AppendLine($"if ({prop.PropertyName} is not null)");
                    Block(() =>
                    {
                        if (prop.IsCollection)
                        {
                            AppendLine($"foreach (var __element in {prop.PropertyName})");
                            Block(() => Accumulate("__element"));
                            return;
                        }

                        Accumulate(prop.PropertyName);
                    });
                }

                AppendLine("return combined;");
            });
        }
        else
        {
            AppendLine("ValidationError? IActionValidationMetadata.ValidateNestedSync() => null;");
        }
    }

    private void RenderValidateNestedAsync()
    {
        if (_model.RunAsync && _model.AsyncNestedProperties.Length > 0)
        {
            XmlSummary(
                "Validates nested properties via IAsyncValidator resolved from DI, without reflection.");
            AppendLine(
                "async global::System.Threading.Tasks.Task<ValidationError?> IActionValidationMetadata.ValidateNestedAsync(global::System.IServiceProvider serviceProvider, global::System.Threading.CancellationToken ct)");
            Block(() =>
            {
                AppendLine("ValidationError? combined = null;");

                foreach (var prop in _model.AsyncNestedProperties)
                {
                    var varName = ToCamelCase(prop.PropertyName);
                    AppendLine(
                        $"var {varName}Validator = serviceProvider.GetService<IAsyncValidator<{prop.PropertyTypeName}>>();");
                    AppendLine($"if ({varName}Validator is not null && {prop.PropertyName} is not null)");
                    Block(() =>
                    {
                        // The validator is for the element: the collection is walked, and each error
                        // adds to the others instead of stopping at the first.
                        if (prop.IsCollection)
                        {
                            AppendLine($"foreach (var __element in {prop.PropertyName})");
                            Block(() =>
                            {
                                AppendLine(
                                    $"var result = await {varName}Validator.ValidateAsync(__element, ct).ConfigureAwait(false);");
                                AppendLine("if (result.IsFailure)");
                                IncreaseIndent();
                                AppendLine(
                                    "combined = combined is null ? result : combined.Value.Combine(result);");
                                DecreaseIndent();
                            });
                            return;
                        }

                        AppendLine(
                            $"var result = await {varName}Validator.ValidateAsync({prop.PropertyName}, ct).ConfigureAwait(false);");
                        AppendLine("if (result.IsFailure)");
                        IncreaseIndent();
                        AppendLine(
                            "combined = combined is null ? result : combined.Value.Combine(result);");
                        DecreaseIndent();
                    });
                }

                AppendLine("return combined;");
            });
        }
        else
        {
            AppendLine(
                "global::System.Threading.Tasks.Task<ValidationError?> IActionValidationMetadata.ValidateNestedAsync(global::System.IServiceProvider serviceProvider, global::System.Threading.CancellationToken ct) => global::System.Threading.Tasks.Task.FromResult<ValidationError?>(null);");
        }
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
