using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>Extended validation methods: Guid, ValidEnum, FutureDate, PastDate, OneOf.</summary>
internal sealed partial class ValidatableTemplate
{
    private void RenderGuidValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        AddUsing("Pragmatic.Validation");
        If($"!GuidAttribute.IsValidGuid({prop.PropertyName})",
            () => RenderWithFor(prop, attr));
    }

    private void RenderValidEnumValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        // Skipped where the type is not an enum: PRAG0220 reports it, and writing the rule anyway
        // would add a CS0453 inside a generated file — a second, more obscure error for the same
        // defect.
        if (!prop.IsEnum)
            return;

        AddUsing("Pragmatic.Validation");
        var isNullable = prop.PropertyType.EndsWith("?");

        if (isNullable)
            If($"{prop.PropertyName}.HasValue && !global::Pragmatic.Validation.Attributes.ValidEnumAttribute.IsValidEnum({prop.PropertyName}.Value)",
                () => RenderWithFor(prop, attr));
        else
            If($"!global::Pragmatic.Validation.Attributes.ValidEnumAttribute.IsValidEnum({prop.PropertyName})",
                () => RenderWithFor(prop, attr));
    }

    private void RenderFutureDateValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var baseType = prop.PropertyType.TrimEnd('?');
        // Nullable date validations are emitted inside an `if (prop is not null)` guard, but a nullable
        // value type is not narrowed to its underlying type — member access still needs `.Value`.
        var expr = DateValueAccessor(prop);
        if (baseType is "global::System.DateTimeOffset" or "System.DateTimeOffset" or "DateTimeOffset")
            If($"{expr} <= global::Pragmatic.Validation.ValidationTimeProvider.Current.GetUtcNow()",
                () => RenderWithFor(prop, attr));
        else
            // Normalize Kind: a Local DateTime is converted to UTC before comparing to a UTC instant.
            If($"{NormalizeDateTimeKind(expr)} <= global::Pragmatic.Validation.ValidationTimeProvider.Current.GetUtcNow().UtcDateTime",
                () => RenderWithFor(prop, attr));
    }

    private void RenderPastDateValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        var baseType = prop.PropertyType.TrimEnd('?');
        var expr = DateValueAccessor(prop);
        if (baseType is "global::System.DateTimeOffset" or "System.DateTimeOffset" or "DateTimeOffset")
            If($"{expr} >= global::Pragmatic.Validation.ValidationTimeProvider.Current.GetUtcNow()",
                () => RenderWithFor(prop, attr));
        else
            // Normalize Kind: a Local DateTime is converted to UTC before comparing to a UTC instant.
            If($"{NormalizeDateTimeKind(expr)} >= global::Pragmatic.Validation.ValidationTimeProvider.Current.GetUtcNow().UtcDateTime",
                () => RenderWithFor(prop, attr));
    }

    // Underlying-value accessor: nullable date properties are guarded by `is not null` upstream, so
    // `.Value` is safe and required for member access / comparison on the non-nullable underlying type.
    private static string DateValueAccessor(PropertyValidationModel prop)
        => prop.IsNullable ? $"{prop.PropertyName}.Value" : prop.PropertyName;

    // A Local DateTime is converted to UTC; Unspecified is treated as UTC (compared as-is).
    private static string NormalizeDateTimeKind(string expr)
        => $"({expr}.Kind == global::System.DateTimeKind.Local ? {expr}.ToUniversalTime() : {expr})";

    private void RenderOneOfValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (attr.AllowedValues.IsDefaultOrEmpty)
            return;

        // For a numeric property, cast each allowed value to the property's type. Integer literals
        // default to `int`, so `[OneOf(1,2,3)]` on a `long`/`short`/`byte`/`decimal` property would
        // otherwise box as `int` and `object.Equals` would be *always false* (the property could
        // never be valid). Enum values are already rendered fully-qualified, so they need no cast.
        var cast = prop.IsNumeric ? $"({prop.PropertyType.TrimEnd('?')})" : string.Empty;
        var conditions = string.Join(" && ", attr.AllowedValues.Select(v => $"!Equals({prop.PropertyName}, {cast}{v})"));
        If(conditions,
            () => RenderWithFor(prop, attr));
    }
}
