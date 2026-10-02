namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a property value is less than another property value.
/// </summary>
/// <remarks>
///     <para>
///         Both values must be comparable types (implement <see cref="IComparable" />).
///         Null values pass (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
///     <para>
///         Common use case: ensuring min is less than max.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record PriceRangeRequest
/// {
///     [Required]
///     [LessThanProperty(nameof(MaxPrice))]
///     public decimal MinPrice { get; init; }
/// 
///     [Required]
///     public decimal MaxPrice { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LessThanPropertyAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the name of the property to compare.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property that this value must be less than.</param>
    public LessThanPropertyAttribute(string otherPropertyName)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
    }

    /// <summary>
    ///     Gets the name of the property that this value must be less than.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.lessthanproperty";

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
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "LessThanProperty");
        // Compare never throws (unlike IComparable.CompareTo on mixed numeric types) and returns
        // null when the values are not comparable, in which case the rule does not apply (valid) —
        // consistent with [GreaterThanProperty].
        return CrossPropertyComparison.Compare(value, otherValue) is not int cmp || cmp < 0;
    }
}