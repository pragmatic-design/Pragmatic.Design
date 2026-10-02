using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Combine extension methods for combining multiple Results into tuple results.
/// </summary>
public static partial class ResultExtensions
{
    // =============================================================================
    // Combine Operations (2-5 Results)
    // =============================================================================

    /// <summary>
    ///     Combines two results into a tuple result.
    /// </summary>
    /// <typeparam name="T1">First value type</typeparam>
    /// <typeparam name="T2">Second value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="r1">First result</param>
    /// <param name="r2">Second result</param>
    /// <returns>A result containing a tuple of both values, or the first error encountered</returns>
    /// <remarks>
    ///     Returns the first error encountered (fails fast).
    ///     Use CollectAll if you need to accumulate all errors.
    ///     <para>
    ///         Relies on the Result invariant: <c>TryGetError</c> only reports a GENUINE failure (non-null
    ///         error), so an uninitialized <c>default(Result)</c> input is never mistaken for a failure and
    ///         never propagated as <c>Failure(null)</c>. A default input instead surfaces loudly when its
    ///         <see cref="Result{TValue, TError}.Value"/> is read.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(T1, T2), TError> Combine<T1, T2, TError>(
        Result<T1, TError> r1,
        Result<T2, TError> r2)
        where TError : IError
    {
        if (r1.TryGetError(out var e1))
            return Result<(T1, T2), TError>.Failure(e1);
        if (r2.TryGetError(out var e2))
            return Result<(T1, T2), TError>.Failure(e2);

        return Result<(T1, T2), TError>.Success((r1.Value, r2.Value));
    }

    /// <summary>
    ///     Combines three results into a tuple result.
    /// </summary>
    /// <typeparam name="T1">First value type</typeparam>
    /// <typeparam name="T2">Second value type</typeparam>
    /// <typeparam name="T3">Third value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="r1">First result</param>
    /// <param name="r2">Second result</param>
    /// <param name="r3">Third result</param>
    /// <returns>A result containing a tuple of all values, or the first error encountered</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(T1, T2, T3), TError> Combine<T1, T2, T3, TError>(
        Result<T1, TError> r1,
        Result<T2, TError> r2,
        Result<T3, TError> r3)
        where TError : IError
    {
        if (r1.TryGetError(out var e1))
            return Result<(T1, T2, T3), TError>.Failure(e1);
        if (r2.TryGetError(out var e2))
            return Result<(T1, T2, T3), TError>.Failure(e2);
        if (r3.TryGetError(out var e3))
            return Result<(T1, T2, T3), TError>.Failure(e3);

        return Result<(T1, T2, T3), TError>.Success((r1.Value, r2.Value, r3.Value));
    }

    /// <summary>
    ///     Combines four results into a tuple result.
    /// </summary>
    /// <typeparam name="T1">First value type</typeparam>
    /// <typeparam name="T2">Second value type</typeparam>
    /// <typeparam name="T3">Third value type</typeparam>
    /// <typeparam name="T4">Fourth value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="r1">First result</param>
    /// <param name="r2">Second result</param>
    /// <param name="r3">Third result</param>
    /// <param name="r4">Fourth result</param>
    /// <returns>A result containing a tuple of all values, or the first error encountered</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(T1, T2, T3, T4), TError> Combine<T1, T2, T3, T4, TError>(
        Result<T1, TError> r1,
        Result<T2, TError> r2,
        Result<T3, TError> r3,
        Result<T4, TError> r4)
        where TError : IError
    {
        if (r1.TryGetError(out var e1))
            return Result<(T1, T2, T3, T4), TError>.Failure(e1);
        if (r2.TryGetError(out var e2))
            return Result<(T1, T2, T3, T4), TError>.Failure(e2);
        if (r3.TryGetError(out var e3))
            return Result<(T1, T2, T3, T4), TError>.Failure(e3);
        if (r4.TryGetError(out var e4))
            return Result<(T1, T2, T3, T4), TError>.Failure(e4);

        return Result<(T1, T2, T3, T4), TError>.Success((r1.Value, r2.Value, r3.Value, r4.Value));
    }

    /// <summary>
    ///     Combines five results into a tuple result.
    /// </summary>
    /// <typeparam name="T1">First value type</typeparam>
    /// <typeparam name="T2">Second value type</typeparam>
    /// <typeparam name="T3">Third value type</typeparam>
    /// <typeparam name="T4">Fourth value type</typeparam>
    /// <typeparam name="T5">Fifth value type</typeparam>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="r1">First result</param>
    /// <param name="r2">Second result</param>
    /// <param name="r3">Third result</param>
    /// <param name="r4">Fourth result</param>
    /// <param name="r5">Fifth result</param>
    /// <returns>A result containing a tuple of all values, or the first error encountered</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(T1, T2, T3, T4, T5), TError> Combine<T1, T2, T3, T4, T5, TError>(
        Result<T1, TError> r1,
        Result<T2, TError> r2,
        Result<T3, TError> r3,
        Result<T4, TError> r4,
        Result<T5, TError> r5)
        where TError : IError
    {
        if (r1.TryGetError(out var e1))
            return Result<(T1, T2, T3, T4, T5), TError>.Failure(e1);
        if (r2.TryGetError(out var e2))
            return Result<(T1, T2, T3, T4, T5), TError>.Failure(e2);
        if (r3.TryGetError(out var e3))
            return Result<(T1, T2, T3, T4, T5), TError>.Failure(e3);
        if (r4.TryGetError(out var e4))
            return Result<(T1, T2, T3, T4, T5), TError>.Failure(e4);
        if (r5.TryGetError(out var e5))
            return Result<(T1, T2, T3, T4, T5), TError>.Failure(e5);

        return Result<(T1, T2, T3, T4, T5), TError>.Success((r1.Value, r2.Value, r3.Value, r4.Value, r5.Value));
    }

    /// <summary>
    ///     Fail-fast combination of an arbitrary number of same-typed results: returns the first error
    ///     encountered, or a list of all values if every result succeeded.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    ///     <c>Success</c> with all values (in input order) if every result succeeded, otherwise
    ///     <c>Failure</c> with the FIRST error encountered.
    /// </returns>
    /// <remarks>
    ///     This is the arbitrary-arity, fail-fast counterpart to the tuple-returning <c>Combine</c>
    ///     overloads (which stop at 5). It differs from <c>CollectAll</c>, which collects ALL errors into
    ///     an <c>AggregateError</c> instead of stopping at the first. An empty sequence returns
    ///     <c>Success</c> with an empty list (vacuous truth).
    /// </remarks>
    public static Result<IReadOnlyList<T>, TError> Combine<T, TError>(
        IEnumerable<Result<T, TError>> results)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(results);

        var values = new List<T>();
        foreach (var result in results)
        {
            if (result.TryGetError(out var error))
                return Result<IReadOnlyList<T>, TError>.Failure(error);
            values.Add(result.Value);
        }

        return Result<IReadOnlyList<T>, TError>.Success(values);
    }
}