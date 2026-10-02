namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async extension methods for VoidResult types.
/// </summary>
public static partial class ResultAsyncExtensions
{
    // =============================================================================
    // VoidResult<E> Tap Extensions
    // =============================================================================

    /// <summary>
    ///     Executes an async action on success without changing the VoidResult.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The void result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<VoidResult<TError>> TapAsync<TError>(
        this VoidResult<TError> result,
        Func<Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.IsSuccess)
            await action().ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Executes an async action on success of a Task&lt;VoidResult&gt;.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the void result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<VoidResult<TError>> TapAsync<TError>(
        this Task<VoidResult<TError>> resultTask,
        Func<Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);

        if (result.IsSuccess)
            await action().ConfigureAwait(false);

        return result;
    }

    // =============================================================================
    // VoidResult<E> OnSuccess Extensions (alias for Tap)
    // =============================================================================

    /// <summary>
    ///     Executes an async action on success without changing the VoidResult.
    ///     Alias for <see cref="TapAsync{TError}(VoidResult{TError},Func{Task},CancellationToken)" />.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The void result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static Task<VoidResult<TError>> OnSuccessAsync<TError>(
        this VoidResult<TError> result,
        Func<Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        return result.TapAsync(action, cancellationToken);
    }

    /// <summary>
    ///     Executes an async action on success of a Task&lt;VoidResult&gt;.
    ///     Alias for <see cref="TapAsync{TError}(Task{VoidResult{TError}},Func{Task},CancellationToken)" />.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the void result</param>
    /// <param name="action">Async action to execute if successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static Task<VoidResult<TError>> OnSuccessAsync<TError>(
        this Task<VoidResult<TError>> resultTask,
        Func<Task> action,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        return resultTask.TapAsync(action, cancellationToken);
    }

    // =============================================================================
    // VoidResult<E> OnFailure Extensions
    // =============================================================================

    /// <summary>
    ///     Executes an async action on the error without changing the VoidResult.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The void result</param>
    /// <param name="action">Async action to execute if failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<VoidResult<TError>> OnFailureAsync<TError>(
        this VoidResult<TError> result,
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
    ///     Executes an async action on the error of a Task&lt;VoidResult&gt;.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the void result</param>
    /// <param name="action">Async action to execute if failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The same result, unchanged</returns>
    public static async Task<VoidResult<TError>> OnFailureAsync<TError>(
        this Task<VoidResult<TError>> resultTask,
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

    // =============================================================================
    // VoidResult<E> OrElse Extensions
    // =============================================================================

    /// <summary>
    ///     Provides an async fallback VoidResult if this result is a failure.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The void result</param>
    /// <param name="fallback">Async function to produce a fallback result from the error</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>This result if successful, or the fallback result if failed</returns>
    public static async Task<VoidResult<TError>> OrElseAsync<TError>(
        this VoidResult<TError> result,
        Func<TError, Task<VoidResult<TError>>> fallback,
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
    ///     Provides an async fallback VoidResult if this Task&lt;VoidResult&gt; is a failure.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="resultTask">The task containing the void result</param>
    /// <param name="fallback">Async function to produce a fallback result from the error</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>This result if successful, or the fallback result if failed</returns>
    public static async Task<VoidResult<TError>> OrElseAsync<TError>(
        this Task<VoidResult<TError>> resultTask,
        Func<TError, Task<VoidResult<TError>>> fallback,
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