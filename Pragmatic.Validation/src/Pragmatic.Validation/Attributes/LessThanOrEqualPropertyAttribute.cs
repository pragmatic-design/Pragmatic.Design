using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a property value is less than or equal to another property value.
/// </summary>
/// <remarks>
///     <para>
///         The inclusive form of <see cref="LessThanPropertyAttribute" />. Both values must be comparable.
///         Null values pass (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial class PriceBand
/// {
///     [LessThanOrEqualProperty(nameof(Max))]
///     public decimal Min { get; init; }
///
///     public decimal Max { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LessThanOrEqualPropertyAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the name of the property to compare.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property this value may not exceed.</param>
    public LessThanOrEqualPropertyAttribute(string otherPropertyName)
    {
        ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
    }

    /// <summary>
    ///     Gets the name of the property this value may not exceed.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.lessthanorequalproperty";

    /// <inheritdoc />
    public override bool RequiresInstance => true;

    /// <inheritdoc />
    public override bool IsValid(object? value) => true;

    /// <inheritdoc />
    public override bool IsValid(object? value, object instance)
    {
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "LessThanOrEqualProperty");
        // Values that cannot be compared leave the rule inapplicable (valid), as for the strict form.
        return CrossPropertyComparison.Compare(value, otherValue) is not int cmp || cmp <= 0;
    }
}
