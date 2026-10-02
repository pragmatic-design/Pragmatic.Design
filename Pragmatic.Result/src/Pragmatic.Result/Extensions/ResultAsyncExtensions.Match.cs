namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async Match extension methods for Result types.
/// </summary>
public static partial class ResultAsyncExtensions
{
    // =============================================================================
    // Task<Result<T,E>> Match Extensions
    // =============================================================================

    /// <summary>
    ///     Pattern matches on a Task&lt;Result&gt;, executing the appropriate function.
    /// </summary>
    /// <typeparam name="TValue">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <typeparam name="TResult">The return type</typeparam>
    /// <param name="resultTask">The task containing the result</param>
    /// <param name="onSuccess">Function to execute if successful</param>
    /// <param name="onFailure">Function to execute if failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the executed function</returns>
    public static async Task<TResult> MatchAsync<TValue, TError, TResult>(
        this Task<Result<TValue, TError>> resultTask,
        Func<TValue, TResult> onSuccess,
        Func<TError, TResult> onFailure,
        CancellationToken cancellationToken = default)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await resultTask.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    // =============================================================================
    // Task<VoidResult<E>> Match Extensions
    // =============================================================================

    /// <param name="resultTask">The task containing the void result</param>
    /// <typeparam name="TError">The error type</typeparam>
    extension<TError>(Task<VoidResult<TError>> resultTask) where TError : IError
    {
        /// <summary>
        ///     Pattern matches on a Task&lt;VoidResult&gt;, executing the appropriate function.
        /// </summary>
        /// <typeparam name="TResult">The return type</typeparam>
        /// <param name="onSuccess">Function to execute if successful</param>
        /// <param name="onFailure">Function to execute if failed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The result of the executed function</returns>
        public async Task<TResult> MatchAsync<TResult>(Func<TResult> onSuccess,
            Func<TError, TResult> onFailure,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resultTask);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            cancellationToken.ThrowIfCancellationRequested();
            var result = await resultTask.ConfigureAwait(false);
            return result.Match(onSuccess, onFailure);
        }

        /// <summary>
        ///     Chains another void operation if the Task&lt;VoidResult&gt; succeeds.
        /// </summary>
        /// <param name="next">The next operation to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The result of the next operation, or the original failure</returns>
        public async Task<VoidResult<TError>> ThenAsync(Func<Task<VoidResult<TError>>> next,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resultTask);
            ArgumentNullException.ThrowIfNull(next);

            cancellationToken.ThrowIfCancellationRequested();
            var result = await resultTask.ConfigureAwait(false);

            if (result.IsSuccess)
                return await next().ConfigureAwait(false);

            return result;
        }
    }
}