using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pragmatic.Result;

namespace Pragmatic.Ensure.Result;

/// <summary>
///     Domain validation checks that return Result instead of throwing.
///     This partial contains collection checks.
/// </summary>
public static partial class Check
{
    /// <summary>
    ///     Checks if a collection is not null and not empty.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrEmpty<T, TError>(
        [NotNullWhen(true)] IEnumerable<T>? value,
        TError error)
        where TError : IError
    {
        return Ensure.IsNotNullOrEmpty(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a collection is not null and not empty.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrEmpty<T, TError>(
        [NotNullWhen(true)] IEnumerable<T>? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsNotNullOrEmpty(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if an array is not null and not empty.
    ///     Optimized overload for arrays (more specific than the <see cref="IEnumerable{T}" /> overload).
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The array to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrEmpty<T, TError>(
        [NotNullWhen(true)] T[]? value,
        TError error)
        where TError : IError
    {
        return Ensure.IsNotNullOrEmpty(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if an array is not null and not empty.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The array to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrEmpty<T, TError>(
        [NotNullWhen(true)] T[]? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsNotNullOrEmpty(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks that a collection contains no duplicate elements.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="error">The error to return if duplicates are found.</param>
    /// <returns>Success if no duplicates found, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NoDuplicates<T, TError>(
        IEnumerable<T>? value,
        TError error)
        where TError : IError
    {
        return Ensure.HasNoDuplicates(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks that a collection contains no duplicate elements.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="errorFactory">Factory to create the error if duplicates are found.</param>
    /// <returns>Success if no duplicates found, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NoDuplicates<T, TError>(
        IEnumerable<T>? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.HasNoDuplicates(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks that a collection does not contain any null elements.
    /// </summary>
    /// <typeparam name="T">The element type (reference type).</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="error">The error to return if any element is null.</param>
    /// <returns>Success if no nulls found, or failure with the error.</returns>
    /// <remarks>
    ///     A null <paramref name="value"/> is treated as a failing collection (returns the error),
    ///     not as an empty sequence. If you want null to be treated as empty/passing,
    ///     guard with a null check before calling this method.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> ContainsNoNull<T, TError>(
        IEnumerable<T?> value,
        TError error)
        where T : class
        where TError : IError
    {
        if (value is null)
            return error;

        foreach (var item in value)
        {
            if (item is null)
                return error;
        }

        return VoidResult<TError>.Success();
    }

    /// <summary>
    ///     Checks that a collection does not contain any null elements.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The element type (reference type).</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="errorFactory">Factory to create the error if any element is null.</param>
    /// <returns>Success if no nulls found, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> ContainsNoNull<T, TError>(
        IEnumerable<T?> value,
        Func<TError> errorFactory)
        where T : class
        where TError : IError
    {
        if (value is null)
            return errorFactory();

        foreach (var item in value)
        {
            if (item is null)
                return errorFactory();
        }

        return VoidResult<TError>.Success();
    }

    /// <summary>
    ///     Checks that a collection count does not exceed the maximum.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="maxCount">The maximum allowed count.</param>
    /// <param name="error">The error to return if count exceeds maximum.</param>
    /// <returns>Success if count is within limit, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> CountNotGreaterThan<T, TError>(
        IReadOnlyCollection<T>? value,
        int maxCount,
        TError error)
        where TError : IError
    {
        if (value is null)
            return error;

        return value.Count <= maxCount
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks that a collection count does not exceed the maximum.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="maxCount">The maximum allowed count.</param>
    /// <param name="errorFactory">Factory to create the error if count exceeds maximum.</param>
    /// <returns>Success if count is within limit, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> CountNotGreaterThan<T, TError>(
        IReadOnlyCollection<T>? value,
        int maxCount,
        Func<TError> errorFactory)
        where TError : IError
    {
        if (value is null)
            return errorFactory();

        return value.Count <= maxCount
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks that a collection count is at least the minimum.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="minCount">The minimum required count.</param>
    /// <param name="error">The error to return if count is below minimum.</param>
    /// <returns>Success if count meets minimum, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> CountNotLessThan<T, TError>(
        IReadOnlyCollection<T>? value,
        int minCount,
        TError error)
        where TError : IError
    {
        if (value is null)
            return error;

        return value.Count >= minCount
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks that a collection count is at least the minimum.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="minCount">The minimum required count.</param>
    /// <param name="errorFactory">Factory to create the error if count is below minimum.</param>
    /// <returns>Success if count meets minimum, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> CountNotLessThan<T, TError>(
        IReadOnlyCollection<T>? value,
        int minCount,
        Func<TError> errorFactory)
        where TError : IError
    {
        if (value is null)
            return errorFactory();

        return value.Count >= minCount
            ? VoidResult<TError>.Success()
            : errorFactory();
    }
}