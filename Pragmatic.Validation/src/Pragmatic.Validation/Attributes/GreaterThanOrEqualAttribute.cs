namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric value is greater than or equal to a specified value.
/// </summary>
/// <remarks>
///     <para>
///         The comparison is inclusive (value can be equal to the bound).
///         Use <see cref="GreaterThanAttribute" /> for exclusive comparison.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateOrderRequest
/// {
///     [GreaterThanOrEqual(1)]
///     public int Quantity { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class GreaterThanOrEqualAttribute : ValidationAttribute
{
    private readonly double _value;

    /// <summary>
    ///     Initializes a new instance with an integer bound.
    /// </summary>
    /// <param name="value">The value that the property must be greater than or equal to.</param>
    public GreaterThanOrEqualAttribute(int value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Initializes a new instance with a double bound.
    /// </summary>
    /// <param name="value">The value that the property must be greater than or equal to.</param>
    public GreaterThanOrEqualAttribute(double value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Gets the value that the property must be greater than or equal to.
    /// </summary>
    public object Value { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.greaterthanorequal";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        var comparison = NumericConversionHelper.CompareToDouble(value, _value);
        return comparison is null || comparison >= 0;
    }
}