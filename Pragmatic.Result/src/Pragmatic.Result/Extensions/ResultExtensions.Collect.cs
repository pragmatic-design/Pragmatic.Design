namespace Pragmatic.Result.Extensions;

/// <summary>
///     CollectAll extension methods for aggregating multiple Results.
/// </summary>
public static partial class ResultExtensions
{
    // =============================================================================
    // CollectAll Operation (Aggregation)
    // =============================================================================

    /// <summary>
    ///     Collects all results, returning either all success values or an aggregate error containing all errors.
    /// </summary>
    /// <typeparam name="T">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="results">The results to collect</param>
    /// <returns>
    ///     A result containing a list of all values if all succeed,
    ///     or an AggregateError containing all errors if any fail.
    /// </returns>
    /// <remarks>
    ///     Unlike Combine, this method collects ALL errors rather than failing fast.
    ///     Use this when you want to show the user all validation errors at once.
    ///     Access individual errors via AggregateError.Errors property.
    ///     <para>
    ///         <b>Empty input:</b> an empty <paramref name="results"/> sequence returns
    ///         <c>Success</c> with an empty list (vacuous truth — "all zero results succeeded").
    ///         This is intentional and consistent with <c>All()</c>-style aggregation; guard for an
    ///         empty input yourself if "nothing to collect" should be treated as a failure in your
    ///         domain.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// var results = new[]
    /// {
    ///     ValidateName(name),
    ///     ValidateEmail(email),
    ///     ValidateAge(age)
    /// };
    ///
    /// var collected = results.CollectAll();
    /// if (collected.IsFailure)
    /// {
    ///     foreach (var error in collected.Error.Errors)
    ///         Console.WriteLine(error.Code);
    /// }
    /// </code>
    /// </example>
    public static Result<IReadOnlyList<T>, AggregateError> CollectAll<T, TError>(
        this IEnumerable<Result<T, TError>> results)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(results);

        var values = new List<T>();
        var errors = new List<IError>();

        foreach (var result in results)
            if (result.TryGetValue(out var value))
                values.Add(value);
            else if (result.TryGetError(out var error))
                errors.Add(error);

        if (errors.Count > 0)
            return Result<IReadOnlyList<T>, AggregateError>.Failure(AggregateError.FromErrors(errors));

        return Result<IReadOnlyList<T>, AggregateError>.Success(values);
    }

    /// <summary>
    ///     Collects all results from a span, returning either all success values or an aggregate error.
    ///     This overload avoids array allocation when called with inline arguments.
    /// </summary>
    /// <typeparam name="T">The value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="results">The results to collect</param>
    /// <returns>
    ///     A result containing a list of all values if all succeed,
    ///     or an AggregateError containing all errors if any fail.
    /// </returns>
    public static Result<IReadOnlyList<T>, AggregateError> CollectAll<T, TError>(
        params ReadOnlySpan<Result<T, TError>> results)
        where TError : IError
    {
        var values = new List<T>(results.Length);
        var errors = new List<IError>();

        foreach (var result in results)
            if (result.TryGetValue(out var value))
                values.Add(value);
            else if (result.TryGetError(out var error))
                errors.Add(error);

        if (errors.Count > 0)
            return Result<IReadOnlyList<T>, AggregateError>.Failure(AggregateError.FromErrors(errors));

        return Result<IReadOnlyList<T>, AggregateError>.Success(values);
    }
}