namespace Pragmatic.Result.Extensions;

/// <summary>
///     Partition extension methods for splitting collections of Results into successes and failures.
/// </summary>
public static partial class ResultExtensions
{
    /// <param name="results">The results to filter</param>
    /// <typeparam name="T">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    extension<T, TError>(IEnumerable<Result<T, TError>> results) where TError : IError
    {
        /// <summary>
        ///     Extracts all success values from a collection of Results, ignoring failures.
        /// </summary>
        /// <returns>An enumerable of success values</returns>
        /// <example>
        ///     <code>
        /// var users = results.GetSuccesses().ToList();
        /// </code>
        /// </example>
        public IEnumerable<T> GetSuccesses()
        {
            ArgumentNullException.ThrowIfNull(results);

            foreach (var result in results)
                if (result.TryGetValue(out var value))
                    yield return value;
        }

        /// <summary>
        ///     Extracts all errors from a collection of Results, ignoring successes.
        /// </summary>
        /// <returns>An enumerable of errors</returns>
        /// <example>
        ///     <code>
        /// var errors = results.GetFailures().ToList();
        /// </code>
        /// </example>
        public IEnumerable<TError> GetFailures()
        {
            ArgumentNullException.ThrowIfNull(results);

            foreach (var result in results)
                if (result.TryGetError(out var error))
                    yield return error;
        }

        /// <summary>
        ///     Splits a collection of Results into two lists: successes and failures.
        /// </summary>
        /// <returns>A tuple of (successes, failures) lists</returns>
        /// <example>
        ///     <code>
        /// var (successes, failures) = results.Partition();
        /// Console.WriteLine($"Processed {successes.Count}, failed {failures.Count}");
        /// </code>
        /// </example>
        public (IReadOnlyList<T> Successes, IReadOnlyList<TError> Failures) Partition()
        {
            ArgumentNullException.ThrowIfNull(results);

            var successes = new List<T>();
            var failures = new List<TError>();

            foreach (var result in results)
                if (result.TryGetValue(out var value))
                    successes.Add(value);
                else if (result.TryGetError(out var error))
                    failures.Add(error);

            return (successes, failures);
        }
    }
}
