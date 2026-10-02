using System.Runtime.CompilerServices;

namespace Pragmatic.Ensure;

/// <summary>
///     Static guard methods for parameter validation.
///     This partial contains Guid, DateTime, and Boolean checks.
/// </summary>
public static partial class Ensure
{

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the Guid is empty.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfEmpty(
        Guid value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Guid cannot be empty.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the enum value is not defined.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotDefined<TEnum>(
        TEnum value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(paramName, value,
                $"Value '{value}' is not a defined member of {typeof(TEnum).Name}.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the DateTime is default (DateTime.MinValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfDefault(
        DateTime value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value == default)
            throw new ArgumentOutOfRangeException(paramName, value, "DateTime cannot be default value.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the DateTime is in the past.
    /// </summary>
    /// <remarks>
    ///     If <paramref name="value"/> has <see cref="DateTimeKind.Local"/>, it is converted to UTC before comparison.
    ///     <see cref="DateTimeKind.Unspecified"/> is treated as UTC.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfInPast(
        DateTime value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        if (utcValue < DateTime.UtcNow)
            throw new ArgumentOutOfRangeException(paramName, value, "DateTime cannot be in the past.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the DateTime is in the future.
    /// </summary>
    /// <remarks>
    ///     If <paramref name="value"/> has <see cref="DateTimeKind.Local"/>, it is converted to UTC before comparison.
    ///     <see cref="DateTimeKind.Unspecified"/> is treated as UTC.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfInFuture(
        DateTime value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        if (utcValue > DateTime.UtcNow)
            throw new ArgumentOutOfRangeException(paramName, value, "DateTime cannot be in the future.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the DateTimeOffset is default.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfDefault(
        DateTimeOffset value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value == default)
            throw new ArgumentOutOfRangeException(paramName, value, "DateTimeOffset cannot be default value.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the DateTimeOffset is in the past.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfInPast(
        DateTimeOffset value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value < DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(paramName, value, "DateTimeOffset cannot be in the past.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the DateTimeOffset is in the future.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfInFuture(
        DateTimeOffset value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value > DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(paramName, value, "DateTimeOffset cannot be in the future.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the value equals the other value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfEqual<T>(
        T value,
        T other,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : IEquatable<T>
    {
        if (EqualityComparer<T>.Default.Equals(value, other))
            throw new ArgumentException($"Value must not equal '{other}'.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the value does not equal the expected value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotEqual<T>(
        T value,
        T expected,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : IEquatable<T>
    {
        if (!EqualityComparer<T>.Default.Equals(value, expected))
            throw new ArgumentException($"Value must equal '{expected}'. Actual: '{value}'.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the struct value is default.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfDefault<T>(
        T value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(value, default))
            throw new ArgumentException("Value must not be default.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the condition is true.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfTrue(
        bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))]
        string? paramName = null)
    {
        if (condition)
            throw new ArgumentException(message ?? "Condition must be false.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the condition is false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfFalse(
        bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))]
        string? paramName = null)
    {
        if (!condition)
            throw new ArgumentException(message ?? "Condition must be true.", paramName);
    }

}