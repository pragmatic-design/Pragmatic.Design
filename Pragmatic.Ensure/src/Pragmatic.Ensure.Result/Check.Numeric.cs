using System.Numerics;
using System.Runtime.CompilerServices;
using Pragmatic.Result;

namespace Pragmatic.Ensure.Result;

/// <summary>
///     Domain validation checks that return Result instead of throwing.
///     This partial contains numeric checks.
/// </summary>
public static partial class Check
{
    /// <summary>
    ///     Checks if a numeric value is positive (greater than zero).
    /// </summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Positive<T, TError>(T value, TError error)
        where T : INumber<T>
        where TError : IError
    {
        return Ensure.IsPositive(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a numeric value is positive (greater than zero).
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Positive<T, TError>(T value, Func<TError> errorFactory)
        where T : INumber<T>
        where TError : IError
    {
        return Ensure.IsPositive(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a numeric value is not negative (zero or positive).
    /// </summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNegative<T, TError>(T value, TError error)
        where T : INumber<T>
        where TError : IError
    {
        return Ensure.IsNotNegative(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a numeric value is not negative (zero or positive).
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNegative<T, TError>(T value, Func<TError> errorFactory)
        where T : INumber<T>
        where TError : IError
    {
        return Ensure.IsNotNegative(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a numeric value is not zero.
    /// </summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotZero<T, TError>(T value, TError error)
        where T : INumber<T>
        where TError : IError
    {
        return Ensure.IsNotZero(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a numeric value is not zero.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotZero<T, TError>(T value, Func<TError> errorFactory)
        where T : INumber<T>
        where TError : IError
    {
        return Ensure.IsNotZero(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a value is within the specified range (inclusive).
    /// </summary>
    /// <typeparam name="T">The comparable type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="min">Minimum allowed value.</param>
    /// <param name="max">Maximum allowed value.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InRange<T, TError>(T value, T min, T max, TError error)
        where T : IComparable<T>
        where TError : IError
    {
        return Ensure.IsInRange(value, min, max)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a value is within the specified range (inclusive).
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The comparable type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="min">Minimum allowed value.</param>
    /// <param name="max">Maximum allowed value.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InRange<T, TError>(T value, T min, T max, Func<TError> errorFactory)
        where T : IComparable<T>
        where TError : IError
    {
        return Ensure.IsInRange(value, min, max)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a value is at least the minimum.
    /// </summary>
    /// <typeparam name="T">The comparable type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="minimum">Minimum allowed value.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> AtLeast<T, TError>(T value, T minimum, TError error)
        where T : IComparable<T>
        where TError : IError
    {
        return Ensure.IsAtLeastMin(value, minimum)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a value is at least the minimum.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The comparable type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="minimum">Minimum allowed value.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> AtLeast<T, TError>(T value, T minimum, Func<TError> errorFactory)
        where T : IComparable<T>
        where TError : IError
    {
        return Ensure.IsAtLeastMin(value, minimum)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a value is at most the maximum.
    /// </summary>
    /// <typeparam name="T">The comparable type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="maximum">Maximum allowed value.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> AtMost<T, TError>(T value, T maximum, TError error)
        where T : IComparable<T>
        where TError : IError
    {
        return Ensure.IsAtMostMax(value, maximum)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a value is at most the maximum.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The comparable type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="maximum">Maximum allowed value.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> AtMost<T, TError>(T value, T maximum, Func<TError> errorFactory)
        where T : IComparable<T>
        where TError : IError
    {
        return Ensure.IsAtMostMax(value, maximum)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }
}