namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async recovery extension methods for Result types (OrElse).
/// </summary>
public static partial class ResultAsyncExtensions
{
    // =============================================================================
    // Result<T,E> OrElse Extensions
    // =============================================================================

    /// <summary>
    ///     Provides an async fallback Result if this result is a failure.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="fallback">Async function to produce a fallback result from the error</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>This result if successful, or the fallback result if failed</returns>
    public static async Task<Result<TValue, TError>> OrElseAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TError, Task<Result<TValue, TError>>> fallback,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(fallback);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.IsSuccess)
            return result;

        return await fallback(result.Error).ConfigureAwait(false);
    }

    /// <summary>
    ///     Provides an async fallback Result if this Task&lt;Result&gt; is a failure.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="fallback">Async function to produce a fallback result from the error</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>This result if successful, or the fallback result if failed</returns>
    public static async Task<Result<TValue, TError>> OrElseAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TError, Task<Result<TValue, TError>>> fallback,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(fallback);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.IsSuccess)
            return result;

        return await fallback(result.Error).ConfigureAwait(false);
    }
}