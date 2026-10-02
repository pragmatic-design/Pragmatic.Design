using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>String length and format validation methods.</summary>
internal sealed partial class ValidatableTemplate
{
    private void RenderMinLengthValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        // A null string is the concern of [Required], not [MinLength]: skip the length check when null
        // (do NOT coerce null to length 0, which would falsely report a min-length violation).
        var size = SizeOf(prop);
        var condition = prop.IsNullable
            ? $"{prop.PropertyName} is not null && {size} < {attr.Value}"
            : $"{size} < {attr.Value}";
        If(condition,
            () => RenderWithFor(prop, attr, $"(\"min\", {attr.Value})"));
    }

    private void RenderMaxLengthValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        // For nullable strings, only check MaxLength when the property is non-null (null can never exceed max).
        var size = SizeOf(prop);
        var condition = prop.IsNullable
            ? $"{prop.PropertyName} is not null && {size} > {attr.Value}"
            : $"{size} > {attr.Value}";
        If(condition,
            () => RenderWithFor(prop, attr, $"(\"max\", {attr.Value})"));
    }

    private void RenderLengthValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        // A null string is the concern of [Required]: skip the range check when null.
        var size = SizeOf(prop);
        var condition = prop.IsNullable
            ? $"{prop.PropertyName} is not null && ({size} < {attr.Value} || {size} > {attr.Value2})"
            : $"{size} < {attr.Value} || {size} > {attr.Value2}";
        If(condition,
            () => RenderWithFor(prop, attr, $"(\"min\", {attr.Value})", $"(\"max\", {attr.Value2})"));
    }

    private void RenderEmailValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        AddUsing("Pragmatic.Validation");
        If($"!EmailAttribute.IsValidEmail({prop.PropertyName})",
            () => RenderWithFor(prop, attr));
    }

    private void RenderPhoneValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        AddUsing("Pragmatic.Validation");
        If($"!PhoneAttribute.IsValidPhone({prop.PropertyName})",
            () => RenderWithFor(prop, attr));
    }

    private void RenderUrlValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        AddUsing("Pragmatic.Validation");
        var schemes = attr.AllowedSchemes.IsDefaultOrEmpty
            ? "null"
            : $"new[] {{ {string.Join(", ", attr.AllowedSchemes.Select(s => $"\"{StringHelper.CSharpLiteral(s)}\""))} }}";
        // RequireAbsolute is passed only when it is false: true is IsValidUrl's default, and leaving it
        // out keeps every validator that never set it byte-for-byte what it was.
        var requireAbsolute = attr.RequireAbsolute ? "" : ", requireAbsolute: false";
        If($"!UrlAttribute.IsValidUrl({prop.PropertyName}, {schemes}{requireAbsolute})",
            () => RenderWithFor(prop, attr));
    }

    private void RenderRegexValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        AddUsing("System.Text.RegularExpressions");
        // Emit a NORMAL (non-verbatim) literal with FULL escaping. A verbatim @"..." would double
        // every backslash in the pattern (\d -> \\d) and let a embedded " break out of the string.
        var pattern = StringHelper.CSharpLiteral(attr.Pattern);
        If($"!Regex.IsMatch({prop.PropertyName} ?? \"\", \"{pattern}\", RegexOptions.None, TimeSpan.FromMilliseconds(250))",
            () => RenderWithFor(prop, attr, $"(\"pattern\", \"{pattern}\")"));
    }

    private void RenderCreditCardValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        AddUsing("Pragmatic.Validation");
        If($"!CreditCardAttribute.IsValidCreditCard({prop.PropertyName})",
            () => RenderWithFor(prop, attr));
    }

    /// <summary>
    ///     How many elements this property has: <c>Length</c> for a string, <c>Count</c> for a
    ///     collection.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The three length rules apply to "a string or collection", as their documentation says,
    ///     so <c>.Length</c> cannot be emitted unconditionally: on a <c>List&lt;T&gt;</c> that is a
    ///     <c>CS1061</c> from inside a generated file.
    /// </remarks>
    private static string SizeOf(PropertyValidationModel prop)
        => prop.IsCollection ? $"{prop.PropertyName}.Count" : $"{prop.PropertyName}.Length";
}
