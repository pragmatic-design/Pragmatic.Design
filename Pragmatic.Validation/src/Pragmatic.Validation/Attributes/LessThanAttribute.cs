namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric value is less than a specified value.
/// </summary>
/// <remarks>
///     <para>
///         The comparison is exclusive (value must be strictly less than the bound).
///         Use <see cref="LessThanOrEqualAttribute" /> for inclusive comparison.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateAgeRequest
/// {
///     [LessThan(150)]  // Reasonable upper bound for age
///     public int Age { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LessThanAttribute : ValidationAttribute
{
    private readonly double _value;

    /// <summary>
    ///     Initializes a new instance with an integer bound.
    /// </summary>
    /// <param name="value">The value that the property must be less than.</param>
    public LessThanAttribute(int value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Initializes a new instance with a double bound.
    /// </summary>
    /// <param name="value">The value that the property must be less than.</param>
    public LessThanAttribute(double value)
    {
        _value = value;
        Value = value;
    }

    /// <summary>
    ///     Gets the value that the property must be less than.
    /// </summary>
    public object Value { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.lessthan";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        var comparison = NumericConversionHelper.CompareToDouble(value, _value);
        // null means an unconvertible (non-numeric) type — a non-applicable value passes, consistent
        // with GreaterThan/GreaterThanOrEqual/LessThanOrEqual and the "null/non-applicable passes" rule.
        return comparison is null || comparison < 0;
    }
}