namespace Pragmatic.Result;

/// <summary>
///     Represents multiple errors collected from parallel operations.
/// </summary>
/// <remarks>
///     <para>
///         Use this when aggregating results from multiple parallel operations
///         where each can fail independently with different error types.
///     </para>
///     <para>
///         <b>Note:</b> This is different from <c>ValidationError</c> which
///         contains multiple issues of the same logical type. AggregateError
///         contains heterogeneous errors from different operations.
///     </para>
///     <para>
///         <b>Usage:</b>
///         <code>
/// var (userResult, orderResult) = await (GetUser(id), GetOrder(id));
/// if (userResult.IsFailure || orderResult.IsFailure)
/// {
///     return AggregateError.From(userResult, orderResult);
/// }
/// </code>
///     </para>
/// </remarks>
public sealed record AggregateError : Error
{
    private readonly IError[] _errors;

    /// <summary>
    ///     Initializes a new AggregateError with the specified errors.
    /// </summary>
    public AggregateError(params IError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
            throw new ArgumentException("At least one error is required.", nameof(errors));

        _errors = errors;
    }

    /// <summary>
    ///     Initializes a new AggregateError from an enumerable of errors.
    /// </summary>
    public AggregateError(IEnumerable<IError> errors)
        : this(errors?.ToArray() ?? throw new ArgumentNullException(nameof(errors)))
    {
    }

    /// <inheritdoc />
    public override string Code => "AGGREGATE_ERROR";

    /// <inheritdoc />
    /// <remarks>
    ///     Returns the highest (most severe) status code among contained errors.
    ///     5xx > 4xx, and within each category, higher codes are considered more severe.
    /// </remarks>
    public override int StatusCode => _errors.Length > 0
        ? _errors.Max(e => e.StatusCode)
        : 500;

    /// <inheritdoc />
    public override string Title => $"Multiple Errors ({_errors.Length})";

    /// <summary>
    ///     Gets the collection of aggregated errors.
    /// </summary>
    public IReadOnlyList<IError> Errors => _errors;

    /// <summary>
    ///     Gets the count of errors in the collection.
    /// </summary>
    public int Count => _errors.Length;

    /// <inheritdoc />
    public override bool IsTransient
    {
        get
        {
            var typed = _errors.OfType<Error>().ToList();
            return typed.Count > 0 && typed.All(e => e.IsTransient);
        }
    }

    /// <inheritdoc />
    public override TimeSpan? RetryAfter
    {
        get
        {
            var retryErrors = _errors.OfType<Error>().Where(e => e.RetryAfter.HasValue).ToList();
            return retryErrors.Count > 0 ? retryErrors.Max(e => e.RetryAfter!.Value) : null;
        }
    }

    /// <summary>
    ///     Creates an AggregateError from an enumerable of errors.
    /// </summary>
    public static AggregateError FromErrors(IEnumerable<IError> errors)
    {
        return new AggregateError(errors);
    }

    /// <summary>
    ///     Creates an AggregateError from multiple results, including only failures.
    /// </summary>
    /// <typeparam name="TValue">The value type shared by all results.</typeparam>
    /// <typeparam name="TError">The concrete error type carried by the results.</typeparam>
    /// <param name="results">The results to inspect.</param>
    /// <returns>
    ///     An <see cref="AggregateError"/> containing every failure's error, or <see langword="null"/>
    ///     when all results succeeded.
    /// </returns>
    /// <remarks>
    ///     Returns <see langword="null"/> when there are no failures — consistent with the tuple
    ///     <see cref="From{T1, TE1, T2, TE2}"/> combinators (a caller checking for
    ///     <see langword="null"/> reads the same across the whole family). Accepts results over any
    ///     concrete error type, so a <c>Result&lt;User, NotFoundError&gt;</c> is passable without
    ///     re-typing to <c>Result&lt;User, IError&gt;</c> first.
    /// </remarks>
    /// <example>
    ///     <code>
    /// Result&lt;User, NotFoundError&gt; user = _users.Find(id);
    /// Result&lt;Order, NotFoundError&gt; order = _orders.Find(id);
    ///
    /// AggregateError? errors = AggregateError.FromFailures(user, order);
    /// if (errors is not null)
    ///     return errors;
    /// </code>
    /// </example>
    public static AggregateError? FromFailures<TValue, TError>(params Result<TValue, TError>[] results)
        where TError : IError
    {
        return FromMany(results);
    }

    /// <summary>
    ///     Creates an AggregateError from two results if any failed.
    /// </summary>
    /// <example>
    ///     <code>
    /// Result&lt;User, NotFoundError&gt; user = _users.Find(userId);
    /// Result&lt;Order, ConflictError&gt; order = _orders.Find(orderId);
    ///
    /// AggregateError? errors = AggregateError.From(user, order);
    /// if (errors is not null)
    ///     return Result&lt;Summary, AggregateError&gt;.Failure(errors);
    /// </code>
    /// </example>
    public static AggregateError? From<T1, TE1, T2, TE2>(
        Result<T1, TE1> r1,
        Result<T2, TE2> r2)
        where TE1 : IError
        where TE2 : IError
    {
        var errors = new List<IError>();
        if (r1.IsFailure)
            errors.Add(r1.Error);
        if (r2.IsFailure)
            errors.Add(r2.Error);
        return errors.Count > 0 ? new AggregateError(errors) : null;
    }

    /// <summary>
    ///     Creates an AggregateError from three results if any failed.
    /// </summary>
    public static AggregateError? From<T1, TE1, T2, TE2, T3, TE3>(
        Result<T1, TE1> r1,
        Result<T2, TE2> r2,
        Result<T3, TE3> r3)
        where TE1 : IError
        where TE2 : IError
        where TE3 : IError
    {
        var errors = new List<IError>();
        if (r1.IsFailure)
            errors.Add(r1.Error);
        if (r2.IsFailure)
            errors.Add(r2.Error);
        if (r3.IsFailure)
            errors.Add(r3.Error);
        return errors.Count > 0 ? new AggregateError(errors) : null;
    }

    /// <summary>
    ///     Creates an AggregateError from four results if any failed.
    /// </summary>
    public static AggregateError? From<T1, TE1, T2, TE2, T3, TE3, T4, TE4>(
        Result<T1, TE1> r1,
        Result<T2, TE2> r2,
        Result<T3, TE3> r3,
        Result<T4, TE4> r4)
        where TE1 : IError
        where TE2 : IError
        where TE3 : IError
        where TE4 : IError
    {
        var errors = new List<IError>();
        if (r1.IsFailure)
            errors.Add(r1.Error);
        if (r2.IsFailure)
            errors.Add(r2.Error);
        if (r3.IsFailure)
            errors.Add(r3.Error);
        if (r4.IsFailure)
            errors.Add(r4.Error);
        return errors.Count > 0 ? new AggregateError(errors) : null;
    }

    /// <summary>
    ///     Creates an AggregateError from five results if any failed.
    /// </summary>
    public static AggregateError? From<T1, TE1, T2, TE2, T3, TE3, T4, TE4, T5, TE5>(
        Result<T1, TE1> r1,
        Result<T2, TE2> r2,
        Result<T3, TE3> r3,
        Result<T4, TE4> r4,
        Result<T5, TE5> r5)
        where TE1 : IError
        where TE2 : IError
        where TE3 : IError
        where TE4 : IError
        where TE5 : IError
    {
        var errors = new List<IError>();
        if (r1.IsFailure)
            errors.Add(r1.Error);
        if (r2.IsFailure)
            errors.Add(r2.Error);
        if (r3.IsFailure)
            errors.Add(r3.Error);
        if (r4.IsFailure)
            errors.Add(r4.Error);
        if (r5.IsFailure)
            errors.Add(r5.Error);
        return errors.Count > 0 ? new AggregateError(errors) : null;
    }

    /// <summary>
    ///     Creates an AggregateError from a variable number of same-type results if any failed.
    /// </summary>
    /// <typeparam name="TValue">The value type shared by all results.</typeparam>
    /// <typeparam name="TError">The concrete error type carried by the results.</typeparam>
    /// <param name="results">The results to inspect.</param>
    /// <returns>
    ///     An <see cref="AggregateError"/> containing every failure's error, or <see langword="null"/>
    ///     when all results succeeded.
    /// </returns>
    public static AggregateError? FromMany<TValue, TError>(params Result<TValue, TError>[] results)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(results);

        var errors = new List<IError>();
        foreach (var result in results)
        {
            if (result.IsFailure)
                errors.Add(result.Error);
        }

        return errors.Count > 0 ? new AggregateError(errors) : null;
    }
}