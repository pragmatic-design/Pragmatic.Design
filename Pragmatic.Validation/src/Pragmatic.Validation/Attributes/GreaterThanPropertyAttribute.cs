using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric property value is greater than another property value.
/// </summary>
/// <remarks>
///     <para>
///         Both values must be comparable numeric types.
///         Null values pass (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
///     <para>
///         Common use case: ensuring end date is after start date, or max is greater than min.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record DateRangeRequest
/// {
///     [Required]
///     public DateTime StartDate { get; init; }
/// 
///     [Required]
///     [GreaterThanProperty(nameof(StartDate))]
///     public DateTime EndDate { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class GreaterThanPropertyAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the name of the property to compare.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property that this value must be greater than.</param>
    public GreaterThanPropertyAttribute(string otherPropertyName)
    {
        ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
    }

    /// <summary>
    ///     Gets the name of the property that this value must be greater than.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.greaterthanproperty";

    /// <inheritdoc />
    public override bool RequiresInstance => true;

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return true;
        // Requires instance
    }

    /// <inheritdoc />
    public override bool IsValid(object? value, object instance)
    {
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "GreaterThanProperty");
        // Compare never throws (unlike IComparable.CompareTo on mixed numeric types) and returns
        // null when the values are not comparable, in which case the rule does not apply (valid).
        return CrossPropertyComparison.Compare(value, otherValue) is not int cmp || cmp > 0;
    }
}