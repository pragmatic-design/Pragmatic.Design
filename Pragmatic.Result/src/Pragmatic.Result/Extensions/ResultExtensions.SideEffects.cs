using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Side-effect extension methods for Result types (Tap, OnSuccess, OnFailure).
/// </summary>
public static partial class ResultExtensions
{
    // =============================================================================
    // Side-Effect Operations (Tap, OnSuccess, OnFailure)
    // =============================================================================

    /// <param name="result">The result to tap</param>
    extension<TValue, TError>(Result<TValue, TError> result) where TError : IError
    {
        /// <summary>
        ///     Executes an action on the success value without changing the result.
        /// </summary>
        /// <param name="action">Action to execute if successful</param>
        /// <returns>The same result, unchanged</returns>
        /// <remarks>
        ///     Use for side effects like logging, metrics, or notifications.
        ///     The action is only executed if the result is successful.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TValue, TError> Tap(Action<TValue> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (result.IsSuccess)
                action(result.Value);

            return result;
        }

        /// <summary>
        ///     Executes an action on the success value without changing the result.
        ///     Alias for <see cref="Tap{TValue,TError}" />.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TValue, TError> OnSuccess(Action<TValue> action)
        {
            return result.Tap(action);
        }

        /// <summary>
        ///     Executes an action on the error without changing the result.
        /// </summary>
        /// <param name="action">Action to execute if failed</param>
        /// <returns>The same result, unchanged</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TValue, TError> OnFailure(Action<TError> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (result.IsFailure)
                action(result.Error);

            return result;
        }
    }
}