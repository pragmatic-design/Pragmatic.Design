using System.Runtime.CompilerServices;
using Pragmatic.Result;

namespace Pragmatic.Ensure.Result;

/// <summary>
///     Domain validation checks that return Result instead of throwing.
///     This partial contains condition, Guid, Enum, and DateTime checks.
/// </summary>
public static partial class Check
{

    /// <summary>
    ///     Checks if a Guid is not empty.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The Guid to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotEmpty<TError>(Guid value, TError error)
        where TError : IError
    {
        return Ensure.IsNotEmpty(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a Guid is not empty.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The Guid to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotEmpty<TError>(Guid value, Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsNotEmpty(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if an enum value is defined.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The enum value to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Defined<TEnum, TError>(TEnum value, TError error)
        where TEnum : struct, Enum
        where TError : IError
    {
        return Ensure.IsDefined(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if an enum value is defined.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The enum value to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Defined<TEnum, TError>(TEnum value, Func<TError> errorFactory)
        where TEnum : struct, Enum
        where TError : IError
    {
        return Ensure.IsDefined(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks a custom condition, returning a failure if the condition is false.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="error">The error to return if the condition is false.</param>
    /// <returns>Success if condition is true, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> That<TError>(bool condition, TError error)
        where TError : IError
    {
        return condition
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks a custom condition, returning a failure if the condition is false.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="errorFactory">Factory to create the error if the condition is false.</param>
    /// <returns>Success if condition is true, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> That<TError>(bool condition, Func<TError> errorFactory)
        where TError : IError
    {
        return condition
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks a custom condition, returning a failure if the condition is true.
    ///     Inverse of <see cref="That{TError}(bool, TError)" />.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="condition">The condition that must be false.</param>
    /// <param name="error">The error to return if the condition is true.</param>
    /// <returns>Success if condition is false, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Not<TError>(bool condition, TError error)
        where TError : IError
    {
        return !condition
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks a custom condition, returning a failure if the condition is true.
    ///     Inverse of <see cref="That{TError}(bool, Func{TError})" />.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="condition">The condition that must be false.</param>
    /// <param name="errorFactory">Factory to create the error if the condition is true.</param>
    /// <returns>Success if condition is false, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Not<TError>(bool condition, Func<TError> errorFactory)
        where TError : IError
    {
        return !condition
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a DateTime is in the past.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTime to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InPast<TError>(DateTime value, TError error)
        where TError : IError
    {
        return Ensure.IsPast(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a DateTime is in the past.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTime to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InPast<TError>(DateTime value, Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsPast(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a DateTime is in the future.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTime to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InFuture<TError>(DateTime value, TError error)
        where TError : IError
    {
        return Ensure.IsFuture(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a DateTime is in the future.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTime to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InFuture<TError>(DateTime value, Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsFuture(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a DateTimeOffset is in the past.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTimeOffset to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InPast<TError>(DateTimeOffset value, TError error)
        where TError : IError
    {
        return Ensure.IsPast(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a DateTimeOffset is in the past.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTimeOffset to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InPast<TError>(DateTimeOffset value, Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsPast(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a DateTimeOffset is in the future.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTimeOffset to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InFuture<TError>(DateTimeOffset value, TError error)
        where TError : IError
    {
        return Ensure.IsFuture(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a DateTimeOffset is in the future.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTimeOffset to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> InFuture<TError>(DateTimeOffset value, Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsFuture(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a DateTime is not the default value.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTime to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotDefault<TError>(DateTime value, TError error)
        where TError : IError
    {
        return Ensure.IsNotDefault(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a DateTime is not the default value.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTime to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotDefault<TError>(DateTime value, Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsNotDefault(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a DateTimeOffset is not the default value.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTimeOffset to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotDefault<TError>(DateTimeOffset value, TError error)
        where TError : IError
    {
        return Ensure.IsNotDefault(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a DateTimeOffset is not the default value.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The DateTimeOffset to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotDefault<TError>(DateTimeOffset value, Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsNotDefault(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a struct value is not default.
    /// </summary>
    /// <typeparam name="T">The struct type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if not default, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotDefault<T, TError>(T value, TError error)
        where T : struct
        where TError : IError
    {
        return Ensure.IsNotDefault(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a struct value is not default.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The struct type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if not default, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotDefault<T, TError>(T value, Func<TError> errorFactory)
        where T : struct
        where TError : IError
    {
        return Ensure.IsNotDefault(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if two values are equal.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value1">First value to compare.</param>
    /// <param name="value2">Second value to compare.</param>
    /// <param name="error">The error to return if values are not equal.</param>
    /// <returns>Success if equal, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Equal<T, TError>(T? value1, T? value2, TError error)
        where T : IEquatable<T>?
        where TError : IError
    {
        return Ensure.AreEqual(value1, value2)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if two values are equal.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value1">First value to compare.</param>
    /// <param name="value2">Second value to compare.</param>
    /// <param name="errorFactory">Factory to create the error if values are not equal.</param>
    /// <returns>Success if equal, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Equal<T, TError>(T? value1, T? value2, Func<TError> errorFactory)
        where T : IEquatable<T>?
        where TError : IError
    {
        return Ensure.AreEqual(value1, value2)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if two values are not equal.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value1">First value to compare.</param>
    /// <param name="value2">Second value to compare.</param>
    /// <param name="error">The error to return if values are equal.</param>
    /// <returns>Success if not equal, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotEqual<T, TError>(T? value1, T? value2, TError error)
        where T : IEquatable<T>?
        where TError : IError
    {
        return Ensure.AreNotEqual(value1, value2)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if two values are not equal.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value1">First value to compare.</param>
    /// <param name="value2">Second value to compare.</param>
    /// <param name="errorFactory">Factory to create the error if values are equal.</param>
    /// <returns>Success if not equal, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotEqual<T, TError>(T? value1, T? value2, Func<TError> errorFactory)
        where T : IEquatable<T>?
        where TError : IError
    {
        return Ensure.AreNotEqual(value1, value2)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

}