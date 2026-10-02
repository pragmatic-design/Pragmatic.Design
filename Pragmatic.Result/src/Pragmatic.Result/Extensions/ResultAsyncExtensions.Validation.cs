namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async validation extension methods for Result types (Ensure).
/// </summary>
public static partial class ResultAsyncExtensions
{
    // =============================================================================
    // Async Conditional Validation (EnsureAsync)
    // =============================================================================

    /// <summary>
    ///     Validates the success value with an async predicate, converting to an error if it fails.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="predicate">Async predicate that must return true for the value to be valid</param>
    /// <param name="errorFactory">Function to create an error if the predicate fails</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result if predicate passes, or a failure with the created error</returns>
    public static async Task<Result<TValue, TError>> EnsureAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, Task<bool>> predicate,
        Func<TValue, TError> errorFactory,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);

        cancellationToken.ThrowIfCancellationRequested();

        if (!result.TryGetValue(out var value))
            return result;

        var isValid = await predicate(value).ConfigureAwait(false);
        return isValid ? result : Result<TValue, TError>.Failure(errorFactory(value));
    }

    /// <summary>
    ///     Validates the success value of a Task&lt;Result&gt; with an async predicate.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="predicate">Async predicate that must return true for the value to be valid</param>
    /// <param name="errorFactory">Function to create an error if the predicate fails</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result if predicate passes, or a failure with the created error</returns>
    public static async Task<Result<TValue, TError>> EnsureAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, Task<bool>> predicate,
        Func<TValue, TError> errorFactory,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (!result.TryGetValue(out var value))
            return result;

        var isValid = await predicate(value).ConfigureAwait(false);
        return isValid ? result : Result<TValue, TError>.Failure(errorFactory(value));
    }
}