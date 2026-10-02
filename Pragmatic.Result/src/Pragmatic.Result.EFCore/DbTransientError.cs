namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Represents a transient database error that may be retried.
/// </summary>
/// <remarks>
///     <para>
///         Transient errors include: deadlocks, timeouts, connection failures,
///         temporary resource unavailability.
///     </para>
///     <para>
///         Status code 503 (Service Unavailable). IsTransient is true for automatic retry detection.
///     </para>
/// </remarks>
public sealed record DbTransientError : Error
{
    /// <inheritdoc />
    /// <remarks>
    ///     The code is intentionally coarse: all transient subtypes (deadlock, timeout, connection
    ///     failure, temporary unavailability) share the same retry semantics and map to a single
    ///     <c>DB_TRANSIENT</c> code. Callers needing to distinguish the specific cause should inspect
    ///     <see cref="ErrorType" /> (and optionally <see cref="Details" />) rather than the code.
    /// </remarks>
    public override string Code => "DB_TRANSIENT";

    /// <inheritdoc />
    public override int StatusCode => 503;

    /// <inheritdoc />
    public override string Title => "Database Temporarily Unavailable";

    /// <inheritdoc />
    public override bool IsTransient => true;

    /// <inheritdoc />
    public override TimeSpan? RetryAfter { get; init; }

    /// <summary>
    ///     Gets the type of transient error.
    /// </summary>
    public DbTransientErrorType ErrorType { get; init; }

    /// <summary>
    ///     Gets additional details about the error.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    ///     Gets whether this is definitely a transient error (vs. possibly permanent).
    /// </summary>
    public bool IsDefinitelyTransient { get; init; }

    /// <summary>
    ///     Creates a deadlock error.
    /// </summary>
    /// <remarks>
    ///     Deadlocks are always transient - retrying with backoff usually succeeds.
    /// </remarks>
    /// <returns>A DbTransientError for deadlock.</returns>
    public static DbTransientError Deadlock()
    {
        return new DbTransientError
        {
            ErrorType = DbTransientErrorType.Deadlock,
            Details = "Transaction was chosen as a deadlock victim. Retry the operation.",
            RetryAfter = TimeSpan.FromMilliseconds(100),
            IsDefinitelyTransient = true
        };
    }

    /// <summary>
    ///     Creates a timeout error.
    /// </summary>
    /// <param name="details">Optional details about the timeout.</param>
    /// <returns>A DbTransientError for timeout.</returns>
    public static DbTransientError Timeout(string? details = null)
    {
        return new DbTransientError
        {
            ErrorType = DbTransientErrorType.Timeout,
            Details = details ?? "Database operation timed out.",
            RetryAfter = TimeSpan.FromSeconds(1),
            IsDefinitelyTransient = false // Could be transient or permanent
        };
    }

    /// <summary>
    ///     Creates a connection error.
    /// </summary>
    /// <param name="details">Optional details about the connection failure.</param>
    /// <returns>A DbTransientError for connection failure.</returns>
    public static DbTransientError ConnectionFailure(string? details = null)
    {
        return new DbTransientError
        {
            ErrorType = DbTransientErrorType.ConnectionFailure,
            Details = details ?? "Failed to connect to database.",
            RetryAfter = TimeSpan.FromSeconds(2),
            IsDefinitelyTransient = false // Could be transient or permanent
        };
    }
}

/// <summary>
///     Types of transient database errors.
/// </summary>
public enum DbTransientErrorType
{
    /// <summary>
    ///     Unknown transient error type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     Deadlock detected - transaction was chosen as victim.
    /// </summary>
    Deadlock = 1,

    /// <summary>
    ///     Operation timed out.
    /// </summary>
    Timeout = 2,

    /// <summary>
    ///     Connection to database failed.
    /// </summary>
    ConnectionFailure = 3
}