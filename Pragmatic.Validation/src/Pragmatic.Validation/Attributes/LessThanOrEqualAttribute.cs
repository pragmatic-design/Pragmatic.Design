namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric value is less than or equal to a specified value.
/// </summary>
/// <remarks>
///     <para>
///         The comparison is inclusive (value can be equal to the bound).
///         Use <see cref="LessThanAttribute" /> for exclusive comparison.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateDiscountRequest
/// {
///     [LessThanOrEqual(100)]  // Max 100% discount
///     public decimal DiscountPercent { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LessThanOrEqualAttribute : ValidationAttribute
{
    private readonly double _value;

    /// <summary>
    ///     Initializes a new instance with an integer bound.
    /// </summary>
    /// <param name="value">The value that the property must be less than or equal to.</param>
    public LessThanOrEqualAttribute(int value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Initializes a new instance with a double bound.
    /// </summary>
    /// <param name="value">The value that the property must be less than or equal to.</param>
    public LessThanOrEqualAttribute(double value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Gets the value that the property must be less than or equal to.
    /// </summary>
    public object Value { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.lessthanorequal";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        var comparison = NumericConversionHelper.CompareToDouble(value, _value);
        return comparison is null || comparison <= 0;
    }
}