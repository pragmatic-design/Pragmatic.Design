using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pragmatic.Ensure;

/// <summary>
///     Static guard methods for parameter validation.
///     This partial contains numeric checks.
/// </summary>
public static partial class Ensure
{

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the value is negative.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNegative<T>(
        T value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : INumber<T>
    {
        if (T.IsNegative(value))
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the value is negative or zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNegativeOrZero<T>(
        T value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : INumber<T>
    {
        if (T.IsNegative(value) || T.IsZero(value))
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the value is zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfZero<T>(
        T value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : INumber<T>
    {
        if (T.IsZero(value))
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be zero.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the value is positive or zero.
    ///     Uses <c>INumber&lt;T&gt;.IsPositive</c> semantics — zero is considered positive
    ///     (its sign bit is clear), so this throws for zero as well.
    ///     Use this when you need a strictly-negative value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfPositiveOrZero<T>(
        T value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : INumber<T>
    {
        if (T.IsPositive(value))
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be negative.");
    }


    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the value is outside the specified range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfOutOfRange<T>(
        T value,
        T min,
        T max,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be between {min} and {max}.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the value is greater than the maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfGreaterThan<T>(
        T value,
        T max,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be less than or equal to {max}.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the value is less than the minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfLessThan<T>(
        T value,
        T min,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be greater than or equal to {min}.");
    }



}