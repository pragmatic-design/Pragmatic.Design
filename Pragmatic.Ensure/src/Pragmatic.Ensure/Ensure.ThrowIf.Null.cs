using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pragmatic.Ensure;

/// <summary>
///     Static guard methods for parameter validation.
///     This partial contains null checks.
/// </summary>
public static partial class Ensure
{
    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the reference type value is null.
    ///     Returns the validated (non-null) value for fluent assignment.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name of the parameter (auto-captured).</param>
    /// <returns>The validated non-null value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    /// <example>
    ///     <code>
    ///     // Fluent assignment — validates and assigns in one statement
    ///     _repository = Ensure.ThrowIfNull(repository);
    ///     </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ThrowIfNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : class
    {
        if (value is null)
            throw new ArgumentNullException(paramName);
        return value;
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the nullable value type has no value.
    ///     Returns the unwrapped value for fluent assignment.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="value">The nullable value to check.</param>
    /// <param name="paramName">The name of the parameter (auto-captured).</param>
    /// <returns>The unwrapped non-null value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    /// <example>
    ///     <code>
    ///     // Fluent assignment — validates and unwraps in one statement
    ///     int count = Ensure.ThrowIfNull(nullableCount);
    ///     </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ThrowIfNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
        where T : struct
    {
        if (!value.HasValue)
            throw new ArgumentNullException(paramName);
        return value.Value;
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if either of the two values is null.
    ///     Returns a tuple of the validated non-null values for fluent assignment.
    /// </summary>
    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <param name="value1">The first value to check.</param>
    /// <param name="value2">The second value to check.</param>
    /// <param name="name1">The name of the first parameter (auto-captured).</param>
    /// <param name="name2">The name of the second parameter (auto-captured).</param>
    /// <returns>A tuple of the validated non-null values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any value is null.</exception>
    /// <example>
    ///     <code>
    ///     var (repo, logger) = Ensure.ThrowIfNull(repository, logger);
    ///     </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (T1, T2) ThrowIfNull<T1, T2>(
        [NotNull] T1? value1,
        [NotNull] T2? value2,
        [CallerArgumentExpression(nameof(value1))]
        string? name1 = null,
        [CallerArgumentExpression(nameof(value2))]
        string? name2 = null)
        where T1 : class
        where T2 : class
    {
        return (ThrowIfNull(value1, name1), ThrowIfNull(value2, name2));
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if any of the two values is null.
    /// </summary>
    /// <param name="value1">The first value to check.</param>
    /// <param name="value2">The second value to check.</param>
    /// <param name="name1">The name of the first parameter (auto-captured).</param>
    /// <param name="name2">The name of the second parameter (auto-captured).</param>
    /// <exception cref="ArgumentNullException">Thrown when any value is null.</exception>
    /// <remarks>
    ///     This overload accepts <c>object?</c> parameters, which boxes value types and loses
    ///     nullable flow analysis ([NotNull]). For nullable flow analysis and fluent assignment,
    ///     prefer individual <see cref="ThrowIfNull{T}(T?, string?)" /> calls or the generic
    ///     <see cref="ThrowIfNull{T1, T2}" /> overload.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfAnyNull(
        [NotNull] object? value1,
        [NotNull] object? value2,
        [CallerArgumentExpression(nameof(value1))]
        string? name1 = null,
        [CallerArgumentExpression(nameof(value2))]
        string? name2 = null)
    {
        ThrowIfNull(value1, name1);
        ThrowIfNull(value2, name2);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if any of the three values is null.
    /// </summary>
    /// <param name="value1">The first value to check.</param>
    /// <param name="value2">The second value to check.</param>
    /// <param name="value3">The third value to check.</param>
    /// <param name="name1">The name of the first parameter (auto-captured).</param>
    /// <param name="name2">The name of the second parameter (auto-captured).</param>
    /// <param name="name3">The name of the third parameter (auto-captured).</param>
    /// <exception cref="ArgumentNullException">Thrown when any value is null.</exception>
    /// <remarks>
    ///     This overload accepts <c>object?</c> parameters, which boxes value types and loses
    ///     nullable flow analysis ([NotNull]). For nullable flow analysis, prefer individual
    ///     <see cref="ThrowIfNull{T}(T?, string?)" /> calls.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfAnyNull(
        [NotNull] object? value1,
        [NotNull] object? value2,
        [NotNull] object? value3,
        [CallerArgumentExpression(nameof(value1))]
        string? name1 = null,
        [CallerArgumentExpression(nameof(value2))]
        string? name2 = null,
        [CallerArgumentExpression(nameof(value3))]
        string? name3 = null)
    {
        ThrowIfNull(value1, name1);
        ThrowIfNull(value2, name2);
        ThrowIfNull(value3, name3);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if any of the four values is null.
    /// </summary>
    /// <param name="value1">The first value to check.</param>
    /// <param name="value2">The second value to check.</param>
    /// <param name="value3">The third value to check.</param>
    /// <param name="value4">The fourth value to check.</param>
    /// <param name="name1">The name of the first parameter (auto-captured).</param>
    /// <param name="name2">The name of the second parameter (auto-captured).</param>
    /// <param name="name3">The name of the third parameter (auto-captured).</param>
    /// <param name="name4">The name of the fourth parameter (auto-captured).</param>
    /// <exception cref="ArgumentNullException">Thrown when any value is null.</exception>
    /// <remarks>
    ///     This overload accepts <c>object?</c> parameters, which boxes value types and loses
    ///     nullable flow analysis ([NotNull]). For nullable flow analysis, prefer individual
    ///     <see cref="ThrowIfNull{T}(T?, string?)" /> calls.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfAnyNull(
        [NotNull] object? value1,
        [NotNull] object? value2,
        [NotNull] object? value3,
        [NotNull] object? value4,
        [CallerArgumentExpression(nameof(value1))]
        string? name1 = null,
        [CallerArgumentExpression(nameof(value2))]
        string? name2 = null,
        [CallerArgumentExpression(nameof(value3))]
        string? name3 = null,
        [CallerArgumentExpression(nameof(value4))]
        string? name4 = null)
    {
        ThrowIfNull(value1, name1);
        ThrowIfNull(value2, name2);
        ThrowIfNull(value3, name3);
        ThrowIfNull(value4, name4);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if any of the five values is null.
    /// </summary>
    /// <param name="value1">The first value to check.</param>
    /// <param name="value2">The second value to check.</param>
    /// <param name="value3">The third value to check.</param>
    /// <param name="value4">The fourth value to check.</param>
    /// <param name="value5">The fifth value to check.</param>
    /// <param name="name1">The name of the first parameter (auto-captured).</param>
    /// <param name="name2">The name of the second parameter (auto-captured).</param>
    /// <param name="name3">The name of the third parameter (auto-captured).</param>
    /// <param name="name4">The name of the fourth parameter (auto-captured).</param>
    /// <param name="name5">The name of the fifth parameter (auto-captured).</param>
    /// <exception cref="ArgumentNullException">Thrown when any value is null.</exception>
    /// <remarks>
    ///     This overload accepts <c>object?</c> parameters, which boxes value types and loses
    ///     nullable flow analysis ([NotNull]). For nullable flow analysis, prefer individual
    ///     <see cref="ThrowIfNull{T}(T?, string?)" /> calls.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfAnyNull(
        [NotNull] object? value1,
        [NotNull] object? value2,
        [NotNull] object? value3,
        [NotNull] object? value4,
        [NotNull] object? value5,
        [CallerArgumentExpression(nameof(value1))]
        string? name1 = null,
        [CallerArgumentExpression(nameof(value2))]
        string? name2 = null,
        [CallerArgumentExpression(nameof(value3))]
        string? name3 = null,
        [CallerArgumentExpression(nameof(value4))]
        string? name4 = null,
        [CallerArgumentExpression(nameof(value5))]
        string? name5 = null)
    {
        ThrowIfNull(value1, name1);
        ThrowIfNull(value2, name2);
        ThrowIfNull(value3, name3);
        ThrowIfNull(value4, name4);
        ThrowIfNull(value5, name5);
    }
}