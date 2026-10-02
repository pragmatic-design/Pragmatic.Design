using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pragmatic.Result;

namespace Pragmatic.Ensure.Result;

/// <summary>
///     Domain validation checks that return Result instead of throwing.
///     This partial contains null checks.
/// </summary>
/// <remarks>
///     <para>
///         Use <c>Check.*</c> methods for domain/business validation where failure
///         is an expected outcome (user input, business rules).
///     </para>
///     <para>
///         Use <c>Ensure.ThrowIf*</c> for programming errors (bugs) where failure
///         indicates a bug in the calling code.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Domain validation - expected failures → Result
/// var found = Check.NotNull(user, NotFoundError.For("User", userId));
/// var valid = Check.NotNullOrEmpty(dto.Email, ValidationError.For("Email", "validation.required"))
///     .Then(() => Check.Email(dto.Email, ValidationError.For("Email", "validation.email")));
/// 
/// // Programming error - unexpected → Exception
/// Ensure.ThrowIfNull(repository); // Bug if null
/// </code>
/// </example>
public static partial class Check
{
    /// <summary>
    ///     Checks if a reference type value is not null, returning a failure Result if it is.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNull<T, TError>(
        [NotNullWhen(true)] T? value,
        TError error)
        where T : class
        where TError : IError
    {
        return value is not null
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a reference type value is not null, returning a failure Result if it is.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNull<T, TError>(
        [NotNullWhen(true)] T? value,
        Func<TError> errorFactory)
        where T : class
        where TError : IError
    {
        return value is not null
            ? VoidResult<TError>.Success()
            : errorFactory();
    }

    /// <summary>
    ///     Checks if a nullable value type has a value, returning a failure Result if it doesn't.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The nullable value to check.</param>
    /// <param name="error">The error to return if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNull<T, TError>(
        [NotNullWhen(true)] T? value,
        TError error)
        where T : struct
        where TError : IError
    {
        return value.HasValue
            ? VoidResult<TError>.Success()
            : error;
    }

    /// <summary>
    ///     Checks if a nullable value type has a value, returning a failure Result if it doesn't.
    ///     Uses a factory for lazy error construction.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="value">The nullable value to check.</param>
    /// <param name="errorFactory">Factory to create the error if the check fails.</param>
    /// <returns>Success if valid, or failure with the error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> NotNull<T, TError>(
        [NotNullWhen(true)] T? value,
        Func<TError> errorFactory)
        where T : struct
        where TError : IError
    {
        return value.HasValue
            ? VoidResult<TError>.Success()
            : errorFactory();
    }
}