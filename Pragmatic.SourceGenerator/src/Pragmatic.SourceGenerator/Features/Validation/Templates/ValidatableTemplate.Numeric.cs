using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>Numeric validation methods.</summary>
internal sealed partial class ValidatableTemplate
{
    private void RenderRangeValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var minValue = FormatNumericValue(attr.Value!, prop.PropertyType);
        var maxValue = FormatNumericValue(attr.Value2!, prop.PropertyType);
        If($"{prop.PropertyName} < {minValue} || {prop.PropertyName} > {maxValue}",
            () => RenderWithFor(prop, attr));
    }

    private static string FormatNumericValue(string value, string targetType)
    {
        var baseType = targetType.TrimEnd('?');
        var hasDecimalSuffix = value.EndsWith("m", StringComparison.OrdinalIgnoreCase);
        var hasFloatSuffix = value.EndsWith("f", StringComparison.OrdinalIgnoreCase);
        var hasDoubleSuffix = value.EndsWith("d", StringComparison.OrdinalIgnoreCase);

        if (baseType is "decimal" or "global::System.Decimal" && hasDecimalSuffix)
            return value;
        if (baseType is "float" or "global::System.Single" && hasFloatSuffix)
            return value;
        if (baseType is "double" or "global::System.Double" &&
            (hasDoubleSuffix || (!hasDecimalSuffix && !hasFloatSuffix)))
            return value;
        if (baseType is "decimal" or "global::System.Decimal")
            return $"(decimal){value}";
        if (baseType is "float" or "global::System.Single")
            return $"(float){value}";
        return value;
    }

    private void RenderPositiveValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        If($"{prop.PropertyName} <= 0",
            () => RenderWithFor(prop, attr));
    }

    private void RenderNegativeValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        If($"{prop.PropertyName} >= 0",
            () => RenderWithFor(prop, attr));
    }

    private void RenderGreaterThanValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var value = FormatNumericValue(attr.Value!, prop.PropertyType);
        If($"{prop.PropertyName} <= {value}",
            () => RenderWithFor(prop, attr, $"(\"value\", {attr.Value})"));
    }

    private void RenderGreaterThanOrEqualValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var value = FormatNumericValue(attr.Value!, prop.PropertyType);
        If($"{prop.PropertyName} < {value}",
            () => RenderWithFor(prop, attr, $"(\"value\", {attr.Value})"));
    }

    private void RenderLessThanValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var value = FormatNumericValue(attr.Value!, prop.PropertyType);
        If($"{prop.PropertyName} >= {value}",
            () => RenderWithFor(prop, attr, $"(\"value\", {attr.Value})"));
    }

    private void RenderLessThanOrEqualValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var value = FormatNumericValue(attr.Value!, prop.PropertyType);
        If($"{prop.PropertyName} > {value}",
            () => RenderWithFor(prop, attr, $"(\"value\", {attr.Value})"));
    }
}
