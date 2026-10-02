namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async Bind extension methods for Result types.
/// </summary>
public static partial class ResultAsyncExtensions
{
    // =============================================================================
    // Task<Result<T,E>> Bind Extensions
    // =============================================================================

    /// <summary>
    ///     Binds the success value of a Task&lt;Result&gt; to a new result-returning function.
    /// </summary>
    /// <typeparam name="TValue">The original value type</typeparam>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="binder">Function that returns a new result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the binder function or the original error</returns>
    public static async Task<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, Result<TNewValue, TError>> binder,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(binder);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);
        return result.Bind(binder);
    }

    /// <summary>
    ///     Binds the success value of a Task&lt;Result&gt; to an async result-returning function.
    /// </summary>
    /// <typeparam name="TValue">The original value type</typeparam>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="binder">Async function that returns a new result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the binder function or the original error</returns>
    public static async Task<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, Task<Result<TNewValue, TError>>> binder,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(binder);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.TryGetValue(out var value))
            return await binder(value).ConfigureAwait(false);

        return Result<TNewValue, TError>.Failure(result.Error);
    }

    /// <summary>
    ///     Binds a Result to an async result-returning function.
    ///     Enables chaining sync Results into async pipelines.
    /// </summary>
    /// <typeparam name="TValue">The original value type</typeparam>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="binder">Async function that returns a new result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the binder function or the original error</returns>
    /// <example>
    ///     <code>
    /// // Start with sync, chain to async
    /// var result = ValidateInput(input)           // Result&lt;Input, Error&gt;
    ///     .BindAsync(SaveToDbAsync, ct)           // Task&lt;Result&lt;Entity, Error&gt;&gt;
    ///     .BindAsync(NotifyAsync, ct);            // Task&lt;Result&lt;Response, Error&gt;&gt;
    /// </code>
    /// </example>
    public static async Task<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, Task<Result<TNewValue, TError>>> binder,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(binder);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.TryGetValue(out var value))
            return await binder(value).ConfigureAwait(false);

        return Result<TNewValue, TError>.Failure(result.Error);
    }
}