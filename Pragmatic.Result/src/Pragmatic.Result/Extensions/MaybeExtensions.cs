using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Extension methods for converting <see cref="Maybe{T}" /> to <see cref="Result{TValue,TError}" />.
/// </summary>
public static class MaybeExtensions
{
    /// <param name="maybe">The option to convert</param>
    /// <typeparam name="T">The value type</typeparam>
    extension<T>(Maybe<T> maybe)
    {
        /// <summary>
        ///     Converts a <see cref="Maybe{T}" /> to a <see cref="Result{TValue,TError}" />.
        ///     Returns Success if the option has a value, Failure with the provided error otherwise.
        /// </summary>
        /// <typeparam name="TError">The error type</typeparam>
        /// <param name="error">The error to return if the option is None</param>
        /// <returns>Success with value if Some, Failure with error if None</returns>
        /// <example>
        ///     <code>
        /// Maybe&lt;User&gt; cached = cache.TryGet("user:123");
        /// Result&lt;User, NotFoundError&gt; result = cached.ToResult(NotFoundError.For("User", "123"));
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T, TError> ToResult<TError>(TError error)
            where TError : IError
        {
            return maybe.HasValue
                ? Result<T, TError>.Success(maybe.Value)
                : Result<T, TError>.Failure(error);
        }

        /// <summary>
        ///     Converts a <see cref="Maybe{T}" /> to a <see cref="Result{TValue,TError}" />
        ///     with lazy error construction. The error factory is only called when the option is None.
        /// </summary>
        /// <typeparam name="TError">The error type</typeparam>
        /// <param name="errorFactory">Factory to create the error (only called if None)</param>
        /// <returns>Success with value if Some, Failure with error if None</returns>
        /// <example>
        ///     <code>
        /// Maybe&lt;User&gt; cached = cache.TryGet("user:123");
        /// Result&lt;User, NotFoundError&gt; result = cached.ToResult(() => NotFoundError.For("User", "123"));
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T, TError> ToResult<TError>(Func<TError> errorFactory)
            where TError : IError
        {
            return maybe.HasValue
                ? Result<T, TError>.Success(maybe.Value)
                : Result<T, TError>.Failure(errorFactory());
        }
    }

    /// <summary>
    ///     Converts a <see cref="Result{TValue,TError}" /> to a <see cref="Maybe{T}" />.
    ///     Returns Some with the value if Success, None if Failure (error is discarded).
    /// </summary>
    /// <typeparam name="T">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result to convert</param>
    /// <returns>Some with value if Success, None if Failure</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Maybe<T> ToMaybe<T, TError>(
        this Result<T, TError> result)
        where TError : IError
    {
        return result.IsSuccess
            ? Maybe<T>.Some(result.Value)
            : Maybe<T>.None();
    }
}
