using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Extension methods for <see cref="IAsyncEnumerable{T}" /> of <see cref="Result{TValue,TError}" />.
/// </summary>
public static class ResultAsyncEnumerableExtensions
{
    /// <param name="source">The async enumerable of results</param>
    /// <typeparam name="T">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    extension<T, TError>(IAsyncEnumerable<Result<T, TError>> source) where TError : IError
    {
        /// <summary>
        ///     Collects all results from an async enumerable, returning either all success values
        ///     or an <see cref="AggregateError" /> with all failures.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Success with all values if all succeeded, or Failure with aggregated errors</returns>
        public async Task<Result<IReadOnlyList<T>, AggregateError>> CollectAsync(CancellationToken cancellationToken = default)
        {
            var successes = new List<T>();
            var failures = new List<IError>();

            await foreach (var result in source.WithCancellation(cancellationToken))
            {
                if (result.IsSuccess)
                    successes.Add(result.Value);
                else
                    failures.Add(result.Error);
            }

            return failures.Count > 0
                ? Result<IReadOnlyList<T>, AggregateError>.Failure(new AggregateError(failures))
                : Result<IReadOnlyList<T>, AggregateError>.Success(successes);
        }

        /// <summary>
        ///     Filters an async enumerable of results, yielding only success values.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An async enumerable containing only the success values</returns>
        public async IAsyncEnumerable<T> FilterSuccessesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var result in source.WithCancellation(cancellationToken))
            {
                if (result.IsSuccess)
                    yield return result.Value;
            }
        }

        /// <summary>
        ///     Filters an async enumerable of results, yielding only error values.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An async enumerable containing only the errors</returns>
        public async IAsyncEnumerable<TError> FilterFailuresAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var result in source.WithCancellation(cancellationToken))
            {
                if (result.IsFailure)
                    yield return result.Error;
            }
        }

        /// <summary>
        ///     Partitions an async enumerable of results into successes and failures.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A tuple of success values and errors</returns>
        public async Task<(IReadOnlyList<T> Successes, IReadOnlyList<TError> Failures)> PartitionAsync(CancellationToken cancellationToken = default)
        {
            var successes = new List<T>();
            var failures = new List<TError>();

            await foreach (var result in source.WithCancellation(cancellationToken))
            {
                if (result.IsSuccess)
                    successes.Add(result.Value);
                else
                    failures.Add(result.Error);
            }

            return (successes, failures);
        }
    }
}
