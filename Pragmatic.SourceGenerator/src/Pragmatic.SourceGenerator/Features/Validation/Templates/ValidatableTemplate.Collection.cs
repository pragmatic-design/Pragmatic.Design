using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>Collection validation methods.</summary>
internal sealed partial class ValidatableTemplate
{
    private void RenderMinCountValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        If($"{GetCollectionCountAccess(prop)} < {attr.Value}",
            () => RenderWithFor(prop, attr, $"(\"min\", {attr.Value})"));
    }

    private void RenderMaxCountValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        If($"{GetCollectionCountAccess(prop)} > {attr.Value}",
            () => RenderWithFor(prop, attr, $"(\"max\", {attr.Value})"));
    }

    private void RenderCountValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var countAccess = GetCollectionCountAccess(prop);
        If($"{countAccess} < {attr.Value} || {countAccess} > {attr.Value2}",
            () => RenderWithFor(prop, attr));
    }

    /// <param name="prop">The collection property.</param>
    /// <param name="nullSafe">
    ///     Reach the count through <c>?.</c>: for a rule rendered where the value may be null and null
    ///     is not its concern.
    /// </param>
    private static string GetCollectionCountAccess(PropertyValidationModel prop, bool nullSafe = false)
    {
        // TrimEnd: a nullable array is `T[]?`, and it still has a Length.
        var count = prop.PropertyType.TrimEnd('?').EndsWith("[]") ? "Length" : "Count";
        return $"{prop.PropertyName}{(nullSafe ? "?." : ".")}{count}";
    }

    private void RenderValidateElements(PropertyValidationModel prop)
    {
        if (string.IsNullOrEmpty(prop.ElementType))
            return;

        Comment("Validate each element");
        AppendLine($"for (var i = 0; i < {GetCollectionCountAccess(prop)}; i++)");
        Block(() =>
        {
            var elementAccess = $"{prop.PropertyName}[i]";
            AppendLine($"if ({elementAccess} is null) continue;");
            AppendLine();
            AppendLine($"var elementError = {elementAccess}.Validate();");
            If("elementError.IsFailure", () =>
            {
                AppendLine("foreach (var issue in elementError.Issues)");
                Block(() =>
                {
                    AppendLine(
                        $"error = error.WithNested(nameof({prop.PropertyName}), i, issue.PropertyPath ?? \"\", issue.MessageKey);");
                });
                if (prop.ValidateElementsStopOnFirst)
                    AppendLine("break;");
            });
        });
    }
}
