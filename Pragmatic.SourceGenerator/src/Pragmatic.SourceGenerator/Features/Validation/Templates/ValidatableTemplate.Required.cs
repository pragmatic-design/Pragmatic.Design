using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>Required and presence validation methods.</summary>
internal sealed partial class ValidatableTemplate
{
    private void RenderRequiredValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (prop.IsString)
        {
            // With [NotEmpty] alongside, the empty string is that rule's to report: the guard narrows
            // to null so that one input gets one answer — validation.required for null,
            // validation.notempty for "" — instead of both keys, or only the wrong one.
            var nullOnly = attr.AllowEmptyStrings
                || prop.Attributes.Any(a => a.Kind == ValidationKind.NotEmpty);
            if (nullOnly)
                AppendLine($"if ({prop.PropertyName} is null)");
            else
                AppendLine($"if (string.IsNullOrEmpty({prop.PropertyName}))");
        }
        else if (prop.IsCollection || prop.IsNullable)
        {
            AppendLine($"if ({prop.PropertyName} is null)");
        }
        else if (IsGuidType(prop.PropertyType))
        {
            AppendLine($"if ({prop.PropertyName} == System.Guid.Empty)");
        }
        else if (prop.IsNonNullableValueType)
        {
            // A non-nullable value type (DateTime, int, enum, ...) can never be null, so it is
            // always "present" — emitting `is null` here would be invalid C# (CS0037). Skip the
            // presence guard entirely; other attributes (e.g. [FutureDate]) still apply.
            return;
        }
        else
        {
            AppendLine($"if ({prop.PropertyName} is null)");
        }

        Block(() =>
        {
            RenderWithFor(prop, attr);
        });
    }

    private static bool IsGuidType(string propertyType)
        => propertyType is "System.Guid" or "global::System.Guid" or "Guid";

    private void RenderNotEmptyValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        // Null-safe on purpose: a presence rule is rendered outside the presence guard, so the value
        // may be null here, and null is [Required]'s to report — not this rule's.
        //
        // ⚠️ On a collection this was `is { Count: 0 } or { Length: 0 }`: a property pattern names
        // members the type must have, so it was CS0117 on a List<T> (no Length) and on an array (no
        // Count) alike — the rule compiled on no collection type at all.
        if (prop.IsString)
            If($"{prop.PropertyName}?.Length == 0",
                () => RenderWithFor(prop, attr));
        else if (prop.IsCollection)
            If($"{GetCollectionCountAccess(prop, nullSafe: true)} == 0",
                () => RenderWithFor(prop, attr));
    }

    private void RenderNotWhiteSpaceValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        If($"string.IsNullOrWhiteSpace({prop.PropertyName})",
            () => RenderWithFor(prop, attr));
    }
}
