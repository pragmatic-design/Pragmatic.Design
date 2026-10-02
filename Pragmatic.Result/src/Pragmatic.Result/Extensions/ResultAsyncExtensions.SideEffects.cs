namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async side-effect extension methods for Result types (Tap, OnSuccess, OnFailure).
/// </summary>
public static partial class ResultAsyncExtensions
{
    // =============================================================================
    // Result<T,E> Tap Extensions
    // =============================================================================

    /// <summary>
    ///     Executes an async action on the success value without changing the result.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    /// <remarks>
    ///     Use for async side effects like logging to an external service or sending notifications.
    ///     The action is only executed if the result is successful.
    /// </remarks>
    public static async Task<Result<TValue, TError>> TapAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.TryGetValue(out var value))
            await action(value).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Executes an async action on the success value of a Task&lt;Result&gt; without changing the result.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<Result<TValue, TError>> TapAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.TryGetValue(out var value))
            await action(value).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Executes an async action on the success value without changing the result, passing the cancellation token to the side effect.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="action">Async action to execute if successful; receives the cancellation token</param>
    /// <param name="cancellationToken">Cancellation token, also passed to the side effect</param>
    /// <returns>The same result, unchanged</returns>
    /// <remarks>
    ///     Use for async side effects like logging to an external service or sending notifications.
    ///     The action is only executed if the result is successful.
    /// </remarks>
    public static async Task<Result<TValue, TError>> TapAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.TryGetValue(out var value))
            await action(value, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Executes an async action on the success value of a Task&lt;Result&gt; without changing the result, passing the cancellation token to the side effect.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="action">Async action to execute if successful; receives the cancellation token</param>
    /// <param name="cancellationToken">Cancellation token, also passed to the side effect</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<Result<TValue, TError>> TapAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.TryGetValue(out var value))
            await action(value, cancellationToken).ConfigureAwait(false);

        return result;
    }

    // =============================================================================
    // Result<T,E> OnFailure Extensions
    // =============================================================================

    /// <summary>
    ///     Executes an async action on the error without changing the result.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="action">Async action to execute if failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<Result<TValue, TError>> OnFailureAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TError, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.TryGetError(out var error))
            await action(error).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Executes an async action on the error of a Task&lt;Result&gt; without changing the result.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="action">Async action to execute if failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<Result<TValue, TError>> OnFailureAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TError, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.TryGetError(out var error))
            await action(error).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Executes an async action on the error without changing the result, passing the cancellation token to the side effect.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="action">Async action to execute if failed; receives the cancellation token</param>
    /// <param name="cancellationToken">Cancellation token, also passed to the side effect</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<Result<TValue, TError>> OnFailureAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TError, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.TryGetError(out var error))
            await action(error, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Executes an async action on the error of a Task&lt;Result&gt; without changing the result, passing the cancellation token to the side effect.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="action">Async action to execute if failed; receives the cancellation token</param>
    /// <param name="cancellationToken">Cancellation token, also passed to the side effect</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<Result<TValue, TError>> OnFailureAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TError, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.TryGetError(out var error))
            await action(error, cancellationToken).ConfigureAwait(false);

        return result;
    }

    // =============================================================================
    // Result<T,E> OnSuccess Extensions (alias for Tap)
    // =============================================================================

    /// <summary>
    ///     Executes an async action on the success value without changing the result.
    ///     Alias for <see cref="TapAsync{TValue,TError}(Result{TValue,TError},Func{TValue,Task},CancellationToken)" />.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static Task<Result<TValue, TError>> OnSuccessAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        return result.TapAsync(action, cancellationToken);
    }

    /// <summary>
    ///     Executes an async action on the success value of a Task&lt;Result&gt; without changing the result.
    ///     Alias for <see cref="TapAsync{TValue,TError}(Task{Result{TValue,TError}},Func{TValue,Task},CancellationToken)" />.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static Task<Result<TValue, TError>> OnSuccessAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        return resultTask.TapAsync(action, cancellationToken);
    }

    /// <summary>
    ///     Executes an async action on the success value without changing the result, passing the cancellation token to the side effect.
    ///     Alias for <see cref="TapAsync{TValue,TError}(Result{TValue,TError},Func{TValue,CancellationToken,Task},CancellationToken)" />.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result</param>
    /// <param name="action">Async action to execute if successful; receives the cancellation token</param>
    /// <param name="cancellationToken">Cancellation token, also passed to the side effect</param>
    /// <returns>The same result, unchanged</returns>
    public static Task<Result<TValue, TError>> OnSuccessAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        return result.TapAsync(action, cancellationToken);
    }

    /// <summary>
    ///     Executes an async action on the success value of a Task&lt;Result&gt; without changing the result, passing the cancellation token to the side effect.
    ///     Alias for <see cref="TapAsync{TValue,TError}(Task{Result{TValue,TError}},Func{TValue,CancellationToken,Task},CancellationToken)" />.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="action">Async action to execute if successful; receives the cancellation token</param>
    /// <param name="cancellationToken">Cancellation token, also passed to the side effect</param>
    /// <returns>The same result, unchanged</returns>
    public static Task<Result<TValue, TError>> OnSuccessAsync<TValue, TError>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        return resultTask.TapAsync(action, cancellationToken);
    }
}