namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric value is within a specified range.
/// </summary>
/// <remarks>
///     <para>
///         Supports all numeric types: int, long, double, decimal, etc.
///         The range is inclusive on both ends.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateProductRequest
/// {
///     [Range(0, 100)]
///     public decimal Price { get; init; }
///
///     [Range(1, 1000)]
///     public int Quantity { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RangeAttribute : ValidationAttribute
{
    private readonly double _maximum;
    private readonly double _minimum;
    private readonly decimal? _minimumDecimal;
    private readonly decimal? _maximumDecimal;

    /// <summary>
    ///     Initializes a new instance with integer bounds.
    /// </summary>
    /// <param name="minimum">The minimum allowed value (inclusive).</param>
    /// <param name="maximum">The maximum allowed value (inclusive).</param>
    public RangeAttribute(int minimum, int maximum)
    {
        Ensure.Ensure.ThrowIfGreaterThan(minimum, maximum);
        _minimum = minimum;
        _maximum = maximum;
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    ///     Initializes a new instance with double bounds.
    /// </summary>
    /// <param name="minimum">The minimum allowed value (inclusive).</param>
    /// <param name="maximum">The maximum allowed value (inclusive).</param>
    public RangeAttribute(double minimum, double maximum)
    {
        Ensure.Ensure.ThrowIfGreaterThan(minimum, maximum);
        _minimum = minimum;
        _maximum = maximum;
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    ///     Initializes a new instance with decimal bounds (no precision loss for decimal values).
    /// </summary>
    /// <param name="minimum">The minimum allowed value (inclusive), as a string to preserve decimal precision.</param>
    /// <param name="maximum">The maximum allowed value (inclusive), as a string to preserve decimal precision.</param>
    public RangeAttribute(string minimum, string maximum)
    {
        _minimumDecimal = decimal.Parse(minimum, System.Globalization.CultureInfo.InvariantCulture);
        _maximumDecimal = decimal.Parse(maximum, System.Globalization.CultureInfo.InvariantCulture);
        Ensure.Ensure.ThrowIfGreaterThan(_minimumDecimal.Value, _maximumDecimal.Value);
        _minimum = (double)_minimumDecimal.Value;
        _maximum = (double)_maximumDecimal.Value;
        Minimum = _minimumDecimal.Value;
        Maximum = _maximumDecimal.Value;
    }

    /// <summary>
    ///     Gets the minimum value (inclusive).
    /// </summary>
    public object Minimum { get; }

    /// <summary>
    ///     Gets the maximum value (inclusive).
    /// </summary>
    public object Maximum { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.range";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        // Use decimal comparison when both bounds were provided as decimals or value is decimal
        if (value is decimal dec)
        {
            var minDec = _minimumDecimal ?? (decimal)_minimum;
            var maxDec = _maximumDecimal ?? (decimal)_maximum;
            return dec >= minDec && dec <= maxDec;
        }

        var minComparison = NumericConversionHelper.CompareToDouble(value, _minimum);
        if (minComparison is null)
            return true;

        var maxComparison = NumericConversionHelper.CompareToDouble(value, _maximum);
        return minComparison >= 0 && maxComparison is not null && maxComparison <= 0;
    }
}
