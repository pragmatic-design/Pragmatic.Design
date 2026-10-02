using System.Runtime.CompilerServices;

namespace Pragmatic.Result;

/// <summary>
///     Static factory methods for creating Result types from common patterns.
/// </summary>
/// <remarks>
///     <para>
///         Provides ergonomic helpers for bridging nullable values and exception-throwing code
///         into the Result pattern. These are convenience wrappers that complement the existing
///         <c>Result&lt;T, E&gt;.Success()</c> and <c>Result&lt;T, E&gt;.Failure()</c> factories.
///     </para>
///     <para>
///         <b>Try/TryAsync:</b> Use at IO boundaries or when integrating with exception-throwing code.
///         For domain logic, prefer returning Result directly.
///     </para>
///     <para>
///         <b>FromNullable:</b> Use when converting nullable values (e.g., from repository lookups)
///         into Result types. The error factory overload avoids allocating the error when the value is non-null.
///     </para>
/// </remarks>
public static class Result
{
    // =============================================================================
    // Try — wrap exception-throwing code into Result
    // =============================================================================

    /// <summary>
    ///     Wraps an operation that may throw into a Result.
    /// </summary>
    /// <typeparam name="TValue">The type of the success value</typeparam>
    /// <typeparam name="TError">The type of the error</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="errorMapper">Function to convert the exception to an error</param>
    /// <returns>Success with value if operation succeeds, Failure with mapped error if it throws</returns>
    /// <remarks>
    ///     Use this at IO boundaries or when integrating with exception-throwing code.
    ///     For domain logic, prefer returning Result directly.
    /// </remarks>
    /// <example>
    ///     <code>
    /// var result = Result.Try(
    ///     () => File.ReadAllText(path),
    ///     ex => new FileError { Path = path, Details = ex });
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> Try<TValue, TError>(
        Func<TValue> operation,
        Func<Exception, TError> errorMapper)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(errorMapper);

        try
        {
            return Result<TValue, TError>.Success(operation());
        }
        catch (Exception ex)
        {
            return Result<TValue, TError>.Failure(errorMapper(ex));
        }
    }

    /// <summary>
    ///     Wraps an async operation that may throw into a Result.
    /// </summary>
    /// <typeparam name="TValue">The type of the success value</typeparam>
    /// <typeparam name="TError">The type of the error</typeparam>
    /// <param name="operation">The async operation to execute (receives CancellationToken)</param>
    /// <param name="errorMapper">Function to convert the exception to an error</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Success with value if operation succeeds, Failure with mapped error if it throws</returns>
    /// <remarks>
    ///     <para>
    ///         The operation receives a CancellationToken to properly support cancellation.
    ///         If the operation is cancelled, OperationCanceledException will be passed to errorMapper.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// var result = await Result.TryAsync(
    ///     async ct => await httpClient.GetStringAsync(url, ct),
    ///     ex => new HttpError { Details = ex },
    ///     cancellationToken);
    /// </code>
    /// </example>
    public static async Task<Result<TValue, TError>> TryAsync<TValue, TError>(
        Func<CancellationToken, Task<TValue>> operation,
        Func<Exception, TError> errorMapper,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(errorMapper);

        try
        {
            var value = await operation(cancellationToken).ConfigureAwait(false);
            return Result<TValue, TError>.Success(value);
        }
        catch (Exception ex)
        {
            return Result<TValue, TError>.Failure(errorMapper(ex));
        }
    }

    /// <summary>
    ///     Wraps a void operation that may throw into a VoidResult.
    /// </summary>
    /// <typeparam name="TError">The type of the error</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="errorMapper">Function to convert the exception to an error</param>
    /// <returns>Success if operation completes, Failure with mapped error if it throws</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Try<TError>(
        Action operation,
        Func<Exception, TError> errorMapper)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(errorMapper);

        try
        {
            operation();
            return VoidResult<TError>.Success();
        }
        catch (Exception ex)
        {
            return VoidResult<TError>.Failure(errorMapper(ex));
        }
    }

    /// <summary>
    ///     Wraps an async void operation that may throw into a VoidResult.
    /// </summary>
    /// <typeparam name="TError">The type of the error</typeparam>
    /// <param name="operation">The async operation to execute (receives CancellationToken)</param>
    /// <param name="errorMapper">Function to convert the exception to an error</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Success if operation completes, Failure with mapped error if it throws</returns>
    public static async Task<VoidResult<TError>> TryAsync<TError>(
        Func<CancellationToken, Task> operation,
        Func<Exception, TError> errorMapper,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(errorMapper);

        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return VoidResult<TError>.Success();
        }
        catch (Exception ex)
        {
            return VoidResult<TError>.Failure(errorMapper(ex));
        }
    }

    // =============================================================================
    // FromNullable — convert nullable values to Result
    // =============================================================================

    /// <summary>
    ///     Creates a Result from a nullable reference type.
    ///     Returns Success if the value is non-null, Failure with the provided error otherwise.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="value">The nullable value</param>
    /// <param name="error">The error to return if value is null</param>
    /// <returns>Success with value if non-null, Failure with error if null</returns>
    /// <example>
    ///     <code>
    /// var user = await repository.FindByIdAsync(id);
    /// var result = Result.FromNullable(user, NotFoundError.For("User", id));
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> FromNullable<TValue, TError>(
        TValue? value,
        TError error)
        where TValue : class
        where TError : IError
    {
        return value is not null
            ? Result<TValue, TError>.Success(value)
            : Result<TValue, TError>.Failure(error);
    }

    /// <summary>
    ///     Creates a Result from a nullable reference type with lazy error construction.
    ///     The error factory is only called when the value is null.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="value">The nullable value</param>
    /// <param name="errorFactory">Factory to create the error (only called if value is null)</param>
    /// <returns>Success with value if non-null, Failure with error if null</returns>
    /// <example>
    ///     <code>
    /// var result = Result.FromNullable(user, () => NotFoundError.For("User", id));
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> FromNullable<TValue, TError>(
        TValue? value,
        Func<TError> errorFactory)
        where TValue : class
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(errorFactory);

        return value is not null
            ? Result<TValue, TError>.Success(value)
            : Result<TValue, TError>.Failure(errorFactory());
    }

    /// <summary>
    ///     Creates a Result from a nullable value type.
    ///     Returns Success if the value has a value, Failure with the provided error otherwise.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="value">The nullable value</param>
    /// <param name="error">The error to return if value is null</param>
    /// <returns>Success with value if has value, Failure with error if null</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> FromNullable<TValue, TError>(
        TValue? value,
        TError error)
        where TValue : struct
        where TError : IError
    {
        return value.HasValue
            ? Result<TValue, TError>.Success(value.Value)
            : Result<TValue, TError>.Failure(error);
    }

    /// <summary>
    ///     Creates a Result from a nullable value type with lazy error construction.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="value">The nullable value</param>
    /// <param name="errorFactory">Factory to create the error (only called if value is null)</param>
    /// <returns>Success with value if has value, Failure with error if null</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> FromNullable<TValue, TError>(
        TValue? value,
        Func<TError> errorFactory)
        where TValue : struct
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(errorFactory);

        return value.HasValue
            ? Result<TValue, TError>.Success(value.Value)
            : Result<TValue, TError>.Failure(errorFactory());
    }
}
