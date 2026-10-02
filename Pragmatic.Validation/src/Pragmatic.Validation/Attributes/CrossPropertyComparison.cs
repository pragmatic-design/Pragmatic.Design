namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Ordering comparison for cross-property validation (<c>[GreaterThanProperty]</c>,
///     <c>[LessThanProperty]</c>). Public because the source generator emits calls to it.
/// </summary>
public static class CrossPropertyComparison
{
    /// <summary>
    ///     Compares two values for ordering without ever throwing.
    /// </summary>
    /// <returns>
    ///     The sign of the comparison (&lt;0, 0, &gt;0), or <c>null</c> when the values cannot be
    ///     compared (either is null, or the types are not orderable). A <c>null</c> result means the
    ///     comparison rule does not apply and validation should treat the property as valid rather
    ///     than throw.
    /// </returns>
    /// <remarks>
    ///     A plain <c>IComparable.CompareTo</c> throws <see cref="System.ArgumentException" /> when the
    ///     two operands are different runtime types (e.g. an <c>int</c> property compared to a
    ///     <c>long</c> property). This helper first tries the exact comparison, then falls back to a
    ///     numeric comparison so cross-width numeric comparisons succeed instead of crashing validation.
    /// </remarks>
    public static int? Compare(object? value, object? other)
    {
        if (value is null || other is null)
            return null;

        if (value is System.IComparable comparable)
        {
            try
            {
                return comparable.CompareTo(other);
            }
            catch (System.ArgumentException)
            {
                // Different runtime types (e.g. int vs long) — fall back to a numeric comparison.
            }
        }

        return NumericConversionHelper.ToComparable(other) is double bound
            ? NumericConversionHelper.CompareToDouble(value, bound)
            : null;
    }
}
