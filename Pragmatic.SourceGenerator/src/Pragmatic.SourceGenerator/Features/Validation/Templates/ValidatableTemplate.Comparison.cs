using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>Comparison and conditional validation methods.</summary>
internal sealed partial class ValidatableTemplate
{
    private void RenderEqualToValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (!OtherPropertyExists(attr))
            return;

        If($"!Equals({prop.PropertyName}, {attr.OtherProperty})",
            () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
    }

    private void RenderNotEqualToValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (!OtherPropertyExists(attr))
            return;

        If($"Equals({prop.PropertyName}, {attr.OtherProperty})",
            () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
    }

    private void RenderGreaterThanPropertyValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (!OtherPropertyExists(attr))
            return;

        // CrossPropertyComparison.Compare never throws (unlike raw IComparable.CompareTo on mixed
        // numeric types, e.g. int vs long) and returns null when the values are not comparable.
        If($"global::Pragmatic.Validation.Attributes.CrossPropertyComparison.Compare({prop.PropertyName}, {attr.OtherProperty}) is int cmpGt{prop.PropertyName} && cmpGt{prop.PropertyName} <= 0",
            () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
    }

    private void RenderLessThanPropertyValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (!OtherPropertyExists(attr))
            return;

        If($"global::Pragmatic.Validation.Attributes.CrossPropertyComparison.Compare({prop.PropertyName}, {attr.OtherProperty}) is int cmpLt{prop.PropertyName} && cmpLt{prop.PropertyName} >= 0",
            () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
    }

    private void RenderGreaterThanOrEqualPropertyValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (!OtherPropertyExists(attr))
            return;

        // The inclusive form: only a lower value is refused.
        If($"global::Pragmatic.Validation.Attributes.CrossPropertyComparison.Compare({prop.PropertyName}, {attr.OtherProperty}) is int cmpGte{prop.PropertyName} && cmpGte{prop.PropertyName} < 0",
            () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
    }

    private void RenderLessThanOrEqualPropertyValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (!OtherPropertyExists(attr))
            return;

        // The inclusive form: only a higher value is refused.
        If($"global::Pragmatic.Validation.Attributes.CrossPropertyComparison.Compare({prop.PropertyName}, {attr.OtherProperty}) is int cmpLte{prop.PropertyName} && cmpLte{prop.PropertyName} > 0",
            () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
    }

    private void RenderRequiredIfValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        // Before the condition, which is where the other property's name is written: the check used
        // to sit inside the inner block, after `Equals(Nonexistent, …)` was already on the page.
        if (!OtherPropertyExists(attr))
            return;

        If($"Equals({attr.OtherProperty}, {attr.ComparisonValue})",
            () => RenderConditionalRequiredCheck(prop, attr));
    }

    private void RenderRequiredIfNotValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (!OtherPropertyExists(attr))
            return;

        If($"!Equals({attr.OtherProperty}, {attr.ComparisonValue})",
            () => RenderConditionalRequiredCheck(prop, attr));
    }

    private void RenderConditionalRequiredCheck(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        if (prop.IsString)
            If($"string.IsNullOrEmpty({prop.PropertyName})",
                () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
        else
            If($"{prop.PropertyName} is null",
                () => RenderWithFor(prop, attr, $"(\"other\", nameof({attr.OtherProperty}))"));
    }

    private void RenderUnknownAttributeValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        Comment($"Runtime validation via {attr.AttributeName}");
        var ctorArgs = attr.CtorArgs.IsDefaultOrEmpty ? string.Empty : string.Join(", ", attr.CtorArgs);
        var attrVar = UnknownAttributeVariable(attr);
        AppendLine($"var {attrVar} = new {attr.AttributeType}({ctorArgs});");
        if (attr.RequiresInstance)
            If($"!{attrVar}.IsValid({prop.PropertyName}, this)",
                () => RenderWithFor(prop, attr));
        else
            If($"!{attrVar}.IsValid({prop.PropertyName})",
                () => RenderWithFor(prop, attr));
    }

    /// <summary>
    ///     Whether the other property this rule names actually exists on the type.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>PRAG0203</c> already reports the missing name, and the rule was written anyway: the
    ///     author got the useful message <b>plus</b> a <c>CS0103</c> from inside a generated file they
    ///     cannot open, about an identifier they never typed. One defect, two errors, and the second
    ///     one leads nowhere. The check is here rather than in the transform because this is the last
    ///     moment before the line exists.
    ///     <para>
    ///         ⚠️ <c>AllPropertyNames</c> and not <c>Properties</c>: the latter holds the properties
    ///         that carry rules, and the property a rule <em>refers to</em> usually carries none — the
    ///         bool a <c>[RequiredIf]</c> reads is exactly that. Asking the wrong list dropped every
    ///         conditional rule whose condition was a plain property.
    ///     </para>
    /// </remarks>
    private bool OtherPropertyExists(ValidationAttributeModel attr)
        => !string.IsNullOrEmpty(attr.OtherProperty)
            && _model.AllPropertyNames.Contains(attr.OtherProperty!);
}
