using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Validation extension methods for Result types (Ensure).
/// </summary>
public static partial class ResultExtensions
{
    // =============================================================================
    // Conditional Validation (Ensure)
    // =============================================================================

    /// <summary>
    ///     Validates the success value with a predicate, converting to an error if it fails.
    /// </summary>
    /// <param name="result">The result to validate</param>
    /// <param name="predicate">Predicate that must return true for the value to be valid</param>
    /// <param name="errorFactory">Function to create an error if the predicate fails</param>
    /// <returns>The same result if predicate passes, or a failure with the created error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> Ensure<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, bool> predicate,
        Func<TValue, TError> errorFactory)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);

        if (result.IsFailure)
            return result;

        return predicate(result.Value)
            ? result
            : Result<TValue, TError>.Failure(errorFactory(result.Value));
    }
}