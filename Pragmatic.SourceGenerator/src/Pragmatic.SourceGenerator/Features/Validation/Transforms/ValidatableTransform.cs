using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Validation.Transforms;

/// <summary>
///     Transform logic for types with validation attributes.
/// </summary>
/// <remarks>
///     Partial class split across:
///     <list type="bullet">
///         <item>ValidatableTransform.cs — main Transform, constants, KnownValidationAttributes</item>
///         <item>ValidatableTransform.Types.cs — type helpers</item>
///         <item>ValidatableTransform.Properties.cs — property extraction</item>
///         <item>ValidatableTransform.Collections.cs — collection analysis</item>
///         <item>ValidatableTransform.Attributes.cs — attribute model creation</item>
///     </list>
/// </remarks>
internal static partial class ValidatableTransform
{
    private const string ValidationAttributeFullName = "Pragmatic.Validation.Attributes.ValidationAttribute";
    private const string ValidateElementsAttributeFullName = "Pragmatic.Validation.Attributes.ValidateElementsAttribute";
    private const string ISyncValidatorFullName = "Pragmatic.Validation.ISyncValidator";

    /// <summary>
    ///     Known validation attribute type names for quick lookup.
    ///     Used by both ValidatableGenerator and ValidatorGenerator.
    /// </summary>
    public static readonly HashSet<string> KnownValidationAttributes = new(StringComparer.Ordinal)
    {
        // Presence
        "Pragmatic.Validation.Attributes.RequiredAttribute",
        "Pragmatic.Validation.Attributes.NotEmptyAttribute",
        "Pragmatic.Validation.Attributes.NotWhiteSpaceAttribute",
        // String
        "Pragmatic.Validation.Attributes.MinLengthAttribute",
        "Pragmatic.Validation.Attributes.MaxLengthAttribute",
        "Pragmatic.Validation.Attributes.LengthAttribute",
        // Format
        "Pragmatic.Validation.Attributes.EmailAttribute",
        "Pragmatic.Validation.Attributes.PhoneAttribute",
        "Pragmatic.Validation.Attributes.UrlAttribute",
        "Pragmatic.Validation.Attributes.RegexAttribute",
        "Pragmatic.Validation.Attributes.CreditCardAttribute",
        // Numeric
        "Pragmatic.Validation.Attributes.RangeAttribute",
        "Pragmatic.Validation.Attributes.PositiveAttribute",
        "Pragmatic.Validation.Attributes.NegativeAttribute",
        "Pragmatic.Validation.Attributes.GreaterThanAttribute",
        "Pragmatic.Validation.Attributes.GreaterThanOrEqualAttribute",
        "Pragmatic.Validation.Attributes.LessThanAttribute",
        "Pragmatic.Validation.Attributes.LessThanOrEqualAttribute",
        // Collection
        "Pragmatic.Validation.Attributes.MinCountAttribute",
        "Pragmatic.Validation.Attributes.MaxCountAttribute",
        "Pragmatic.Validation.Attributes.CountAttribute",
        // Comparison
        "Pragmatic.Validation.Attributes.EqualToAttribute",
        "Pragmatic.Validation.Attributes.NotEqualToAttribute",
        "Pragmatic.Validation.Attributes.GreaterThanPropertyAttribute",
        "Pragmatic.Validation.Attributes.LessThanPropertyAttribute",
        "Pragmatic.Validation.Attributes.GreaterThanOrEqualPropertyAttribute",
        "Pragmatic.Validation.Attributes.LessThanOrEqualPropertyAttribute",
        // Conditional
        "Pragmatic.Validation.Attributes.RequiredIfAttribute",
        "Pragmatic.Validation.Attributes.RequiredIfNotAttribute",
        // Format (extended)
        "Pragmatic.Validation.Attributes.GuidAttribute",
        "Pragmatic.Validation.Attributes.ValidEnumAttribute",
        // Date
        "Pragmatic.Validation.Attributes.FutureDateAttribute",
        "Pragmatic.Validation.Attributes.PastDateAttribute",
        // Set
        "Pragmatic.Validation.Attributes.OneOfAttribute"
    };

    /// <summary>
    ///     Checks if a type has validation attributes on its properties.
    ///     Used by ValidatorGenerator to determine if sync validation exists.
    /// </summary>
    public static bool HasValidationAttributes(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
            if (member is IPropertySymbol property)
                foreach (var attr in property.GetAttributes())
                    if (attr.AttributeClass is not null &&
                        KnownValidationAttributes.Contains(attr.AttributeClass.ToDisplayString()))
                        return true;

        return false;
    }
}
