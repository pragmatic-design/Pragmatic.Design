namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric value is greater than a specified value.
/// </summary>
/// <remarks>
///     <para>
///         The comparison is exclusive (value must be strictly greater than the bound).
///         Use <see cref="GreaterThanOrEqualAttribute" /> for inclusive comparison.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateDiscountRequest
/// {
///     [GreaterThan(0)]
///     public decimal DiscountPercent { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class GreaterThanAttribute : ValidationAttribute
{
    private readonly double _value;

    /// <summary>
    ///     Initializes a new instance with an integer bound.
    /// </summary>
    /// <param name="value">The value that the property must be greater than.</param>
    public GreaterThanAttribute(int value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Initializes a new instance with a double bound.
    /// </summary>
    /// <param name="value">The value that the property must be greater than.</param>
    public GreaterThanAttribute(double value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Gets the value that the property must be greater than.
    /// </summary>
    public object Value { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.greaterthan";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        var comparison = NumericConversionHelper.CompareToDouble(value, _value);
        return comparison is null || comparison > 0;
    }
}