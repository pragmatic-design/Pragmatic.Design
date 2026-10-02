namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async Map extension methods for Result types.
/// </summary>
public static partial class ResultAsyncExtensions
{
    // =============================================================================
    // Task<Result<T,E>> Map Extensions
    // =============================================================================

    /// <summary>
    ///     Maps the success value of a Task&lt;Result&gt; to a new type.
    /// </summary>
    /// <typeparam name="TValue">The original value type</typeparam>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="mapper">Function to map the value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new result with the mapped value</returns>
    public static async Task<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, TNewValue> mapper,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(mapper);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(mapper);
    }

    /// <summary>
    ///     Maps the success value of a Task&lt;Result&gt; using an async mapper.
    /// </summary>
    /// <typeparam name="TValue">The original value type</typeparam>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="mapper">Async function to map the value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new result with the mapped value</returns>
    public static async Task<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, Task<TNewValue>> mapper,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(mapper);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.TryGetValue(out var value))
        {
            var newValue = await mapper(value).ConfigureAwait(false);
            return Result<TNewValue, TError>.Success(newValue);
        }

        return Result<TNewValue, TError>.Failure(result.Error);
    }

    /// <summary>
    ///     Maps a Result using an async mapper function.
    ///     Enables chaining sync Results into async pipelines.
    /// </summary>
    /// <typeparam name="TValue">The original value type</typeparam>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="mapper">Async function to map the value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new result with the mapped value</returns>
    public static async Task<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, Task<TNewValue>> mapper,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(mapper);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.TryGetValue(out var value))
        {
            var newValue = await mapper(value).ConfigureAwait(false);
            return Result<TNewValue, TError>.Success(newValue);
        }

        return Result<TNewValue, TError>.Failure(result.Error);
    }
}