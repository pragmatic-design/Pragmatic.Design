using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Recovery extension methods for Result types (OrElse, Recover).
/// </summary>
public static partial class ResultExtensions
{
    // =============================================================================
    // Error Recovery (OrElse, Recover)
    // =============================================================================

    /// <param name="result">The result</param>
    extension<TValue, TError>(Result<TValue, TError> result) where TError : IError
    {
        /// <summary>
        ///     Provides a fallback Result if this result is a failure.
        /// </summary>
        /// <param name="fallback">Function to produce a fallback result from the error</param>
        /// <returns>This result if successful, or the fallback result if failed</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TValue, TError> OrElse(Func<TError, Result<TValue, TError>> fallback)
        {
            ArgumentNullException.ThrowIfNull(fallback);

            return result.IsSuccess ? result : fallback(result.Error);
        }

        /// <summary>
        ///     Provides a fallback value if this result is a failure.
        /// </summary>
        /// <param name="fallback">Function to produce a fallback value from the error</param>
        /// <returns>This result if successful, or a success with the fallback value if failed</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TValue, TError> Recover(Func<TError, TValue> fallback)
        {
            ArgumentNullException.ThrowIfNull(fallback);

            return result.IsSuccess ? result : Result<TValue, TError>.Success(fallback(result.Error));
        }
    }
}