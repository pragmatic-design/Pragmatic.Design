namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Shared numeric conversion and comparison logic for validation attributes.
/// </summary>
internal static class NumericConversionHelper
{
    /// <summary>
    ///     Attempts to convert a boxed numeric value to an IComparable for comparison.
    ///     Returns the value as-is for decimal (avoiding precision loss), or converts to double.
    /// </summary>
    internal static IComparable? ToComparable(object? value) => value switch
    {
        null => null,
        int i => (double)i,
        long l => (double)l,
        short s => (double)s,
        byte b => (double)b,
        sbyte sb => (double)sb,
        uint ui => (double)ui,
        ulong ul => (double)ul,
        ushort us => (double)us,
        float f => (double)f,
        double d => d,
        decimal dec => dec,
        _ => null
    };

    /// <summary>
    ///     Compares a numeric value against a double bound.
    ///     Integer types (long/ulong/int/...) are compared via integer semantics when the
    ///     bound is a whole number in range — this preserves precision beyond 2^53 which
    ///     plain <see cref="double"/> comparison would silently lose.
    /// </summary>
    internal static int? CompareToDouble(object? value, double bound)
    {
        if (value is null) return null;

        // Integer fast paths — avoid (double) cast when both sides can stay integer.
        switch (value)
        {
            case long l: return CompareInt64ToDouble(l, bound);
            case ulong ul: return CompareUInt64ToDouble(ul, bound);
            case int i: return CompareInt64ToDouble(i, bound);
            case uint ui: return CompareInt64ToDouble(ui, bound);
            case short s: return CompareInt64ToDouble(s, bound);
            case ushort us: return CompareInt64ToDouble(us, bound);
            case byte b: return CompareInt64ToDouble(b, bound);
            case sbyte sb: return CompareInt64ToDouble(sb, bound);
            case decimal dec: return dec.CompareTo((decimal)bound);
            case float f: return ((double)f).CompareTo(bound);
            case double d: return d.CompareTo(bound);
            default: return null;
        }
    }

    private static int CompareInt64ToDouble(long value, double bound)
    {
        // Non-finite bounds: delegate to double semantics (NaN returns 0 via CompareTo).
        if (!double.IsFinite(bound))
            return ((double)value).CompareTo(bound);

        // Bound fits in long with no fractional part → exact integer compare.
        if (bound >= long.MinValue && bound <= long.MaxValue && bound == Math.Truncate(bound))
            return value.CompareTo((long)bound);

        // Bound out of range of long or fractional → compare via double of value.
        // Precision may be lost here but only when the caller supplied a bound
        // that cannot be represented as an integer anyway.
        return ((double)value).CompareTo(bound);
    }

    private static int CompareUInt64ToDouble(ulong value, double bound)
    {
        if (!double.IsFinite(bound))
            return ((double)value).CompareTo(bound);

        if (bound < 0) return 1; // any ulong > any negative bound

        // ulong.MaxValue (2^64-1) cannot be represented exactly as double; the nearest double is
        // exactly 2^64 (and "2^64 - 1.0" rounds straight back to 2^64 — double spacing there is
        // 2048). Guard STRICTLY below 2^64 so (ulong)bound never saturates: a saturated cast would
        // compare value==ulong.MaxValue as equal to a bound that is actually greater.
        const double ulongUpperExclusive = (double)(1UL << 63) * 2.0; // exactly 2^64
        if (bound < ulongUpperExclusive && bound == Math.Truncate(bound))
            return value.CompareTo((ulong)bound);

        return ((double)value).CompareTo(bound);
    }
}
