using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pragmatic.Result;

namespace Pragmatic.Ensure.Result;

/// <summary>
///     Domain validation checks that return Result instead of throwing.
///     This partial contains string checks.
/// </summary>
public static partial class Check
{
    /// <summary>
    ///     Checks if a string is not null or empty.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrEmpty<TError>(
        [NotNullWhen(true)] string? value,
        TError error)
        where TError : IError
    {
        return !string.IsNullOrEmpty(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string is not null or empty.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrEmpty<TError>(
        [NotNullWhen(true)] string? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return !string.IsNullOrEmpty(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a string is not null, empty, or whitespace.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrWhiteSpace<TError>(
        [NotNullWhen(true)] string? value,
        TError error)
        where TError : IError
    {
        return !string.IsNullOrWhiteSpace(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string is not null, empty, or whitespace.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNullOrWhiteSpace<TError>(
        [NotNullWhen(true)] string? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return !string.IsNullOrWhiteSpace(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a string length is within the specified range.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="minLength">Minimum allowed length.</param>
    /// <param name="maxLength">Maximum allowed length.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    /// <remarks>Returns success if value is null (null-safe).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> LengthInRange<TError>(
        string? value,
        int minLength,
        int maxLength,
        TError error)
        where TError : IError
    {
        return value is null || (value.Length >= minLength && value.Length <= maxLength)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string length is within the specified range.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="minLength">Minimum allowed length.</param>
    /// <param name="maxLength">Maximum allowed length.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    /// <remarks>Returns success if value is null (null-safe).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> LengthInRange<TError>(
        string? value,
        int minLength,
        int maxLength,
        Func<TError> errorFactory)
        where TError : IError
    {
        return value is null || (value.Length >= minLength && value.Length <= maxLength)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a string is a valid email address.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Email<TError>(
        [NotNullWhen(true)] string? value,
        TError error)
        where TError : IError
    {
        return Ensure.IsEmail(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string is a valid email address.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Email<TError>(
        [NotNullWhen(true)] string? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsEmail(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a string is a valid URL.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <param name="requireHttps">If true, only HTTPS URLs are valid.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Url<TError>(
        [NotNullWhen(true)] string? value,
        TError error,
        bool requireHttps = false)
        where TError : IError
    {
        return Ensure.IsUrl(value, requireHttps)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string is a valid URL.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <param name="requireHttps">If true, only HTTPS URLs are valid.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Url<TError>(
        [NotNullWhen(true)] string? value,
        Func<TError> errorFactory,
        bool requireHttps = false)
        where TError : IError
    {
        return Ensure.IsUrl(value, requireHttps)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a string is a valid phone number.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Phone<TError>(
        [NotNullWhen(true)] string? value,
        TError error)
        where TError : IError
    {
        return Ensure.IsPhone(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string is a valid phone number.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Phone<TError>(
        [NotNullWhen(true)] string? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsPhone(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a string is a valid credit card number (Luhn algorithm, ISO/IEC 7812).
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> CreditCard<TError>(
        [NotNullWhen(true)] string? value,
        TError error)
        where TError : IError
    {
        return Ensure.IsCreditCard(value)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string is a valid credit card number (Luhn algorithm, ISO/IEC 7812).
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> CreditCard<TError>(
        [NotNullWhen(true)] string? value,
        Func<TError> errorFactory)
        where TError : IError
    {
        return Ensure.IsCreditCard(value)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks that a string does not contain the specified substring.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> DoesNotContain<TError>(
        string? value,
        string substring,
        TError error)
        where TError : IError
    {
        return value is null || Ensure.DoesNotContain(value, substring)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks that a string does not contain the specified substring.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> DoesNotContain<TError>(
        string? value,
        string substring,
        Func<TError> errorFactory)
        where TError : IError
    {
        return value is null || Ensure.DoesNotContain(value, substring)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks that a string does not start with the specified prefix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> DoesNotStartWith<TError>(
        string? value,
        string prefix,
        TError error)
        where TError : IError
    {
        return value is null || Ensure.DoesNotStartWith(value, prefix)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks that a string does not start with the specified prefix.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> DoesNotStartWith<TError>(
        string? value,
        string prefix,
        Func<TError> errorFactory)
        where TError : IError
    {
        return value is null || Ensure.DoesNotStartWith(value, prefix)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks that a string does not end with the specified suffix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> DoesNotEndWith<TError>(
        string? value,
        string suffix,
        TError error)
        where TError : IError
    {
        return value is null || Ensure.DoesNotEndWith(value, suffix)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks that a string does not end with the specified suffix.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> DoesNotEndWith<TError>(
        string? value,
        string suffix,
        Func<TError> errorFactory)
        where TError : IError
    {
        return value is null || Ensure.DoesNotEndWith(value, suffix)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a string matches a regex pattern.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    /// <remarks>Returns success if value is null (null-safe).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Match<TError>(
        string? value,
        [StringSyntax(StringSyntaxAttribute.Regex)]
        string pattern,
        TError error)
        where TError : IError
    {
        // Null-safe: null values pass the check (use NotNullOrEmpty first for required fields)
        return value is null || Ensure.IsMatch(value, pattern)
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a string matches a regex pattern.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The string to check.</param>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    /// <remarks>Returns success if value is null (null-safe).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Match<TError>(
        string? value,
        [StringSyntax(StringSyntaxAttribute.Regex)]
        string pattern,
        Func<TError> errorFactory)
        where TError : IError
    {
        // Null-safe: null values pass the check (use NotNullOrEmpty first for required fields)
        return value is null || Ensure.IsMatch(value, pattern)
            ? VoidResult<TError>.Success()
            : errorFactory();
    }
}