using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>Nested object validation methods.</summary>
internal sealed partial class ValidatableTemplate
{
    private void RenderValidateNestedObject(PropertyValidationModel prop)
    {
        Comment($"Validate nested {prop.PropertyName}");

        if (prop.IsNullable)
        {
            AppendLine($"if ({prop.PropertyName} is not null)");
            Block(RenderNestedValidateCall);
        }
        else
        {
            RenderNestedValidateCall();
        }

        return;

        void RenderNestedValidateCall()
        {
            AppendLine($"var nestedError = {prop.PropertyName}.Validate();");
            If("nestedError.IsFailure", () =>
            {
                AppendLine("foreach (var issue in nestedError.Issues)");
                Block(() =>
                {
                    AppendLine(
                        $"error = error.WithNested(nameof({prop.PropertyName}), issue.PropertyPath ?? \"\", issue.MessageKey);");
                });
            });
        }
    }
}
