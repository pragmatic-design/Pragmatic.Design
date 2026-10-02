using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Value extraction extension methods for Result types (GetValueOrDefault, GetValueOrThrow).
/// </summary>
public static partial class ResultExtensions
{
    // =============================================================================
    // Value Extraction (GetValueOrDefault, GetValueOrThrow)
    // =============================================================================

    /// <param name="result">The result</param>
    extension<TValue, TError>(Result<TValue, TError> result) where TError : IError
    {
        /// <summary>
        ///     Gets the success value or a default value if the result is a failure.
        /// </summary>
        /// <param name="defaultValue">The value to return if this is a failure</param>
        /// <returns>The success value or the default value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetValueOrDefault(TValue defaultValue)
        {
            return result.IsSuccess ? result.Value : defaultValue;
        }

        /// <summary>
        ///     Gets the success value or a lazily-computed default value if the result is a failure.
        /// </summary>
        /// <param name="defaultFactory">Factory function to produce the default value</param>
        /// <returns>The success value or the result of the factory</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetValueOrDefault(Func<TValue> defaultFactory)
        {
            ArgumentNullException.ThrowIfNull(defaultFactory);
            return result.IsSuccess ? result.Value : defaultFactory();
        }

        /// <summary>
        ///     Gets the success value or throws an InvalidOperationException with error details.
        /// </summary>
        /// <returns>The success value</returns>
        /// <exception cref="InvalidOperationException">Thrown if the result is a failure.</exception>
        /// <remarks>
        ///     The thrown message includes <see cref="IError.Code"/> — a stable, public part of the error
        ///     taxonomy (e.g. <c>NOT_FOUND</c>), NOT a secret — to make the programming error (calling this on
        ///     a failure without checking) diagnosable. It deliberately does NOT include
        ///     <see cref="IError.Description"/>, which may carry sensitive detail. As with any exception,
        ///     preventing raw propagation to end clients is the responsibility of the host's error-handling
        ///     middleware, not this guard method.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetValueOrThrow()
        {
            if (result.IsFailure)
                throw new InvalidOperationException(
                    $"Result is a failure. Error code: {result.Error.Code}. " +
                    "Use Match, GetValueOrDefault, or check IsSuccess before accessing Value.");

            return result.Value;
        }
    }
}