using System.Text.Json.Serialization;
using Pragmatic.Result;

namespace Pragmatic.Persistence.Query.Results;

/// <summary>
///     Base class for all query-related errors.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Database), "database")]
[JsonDerivedType(typeof(Timeout), "timeout")]
[JsonDerivedType(typeof(Connection), "connection")]
[JsonDerivedType(typeof(InvalidParameters), "invalidParameters")]
[JsonDerivedType(typeof(Unauthorized), "unauthorized")]
public abstract record QueryError : Error
{
    /// <summary>
    ///     Database error during query execution.
    /// </summary>
    /// <remarks>
    ///     Use for SQL errors, connection failures, constraint violations, etc.
    /// </remarks>
    public sealed record Database : QueryError
    {
        /// <inheritdoc />
        public override string Code => "QUERY_DATABASE_ERROR";

        /// <inheritdoc />
        public override int StatusCode => 500;

        /// <summary>
        ///     The error message from the database provider.
        /// </summary>
        public required string Message { get; init; }

        /// <summary>
        ///     The inner exception, if available.
        ///     Not serialized — exceptions are not cache-safe.
        /// </summary>
        [JsonIgnore]
        public Exception? Inner { get; init; }
    }

    /// <summary>
    ///     Query execution timed out.
    /// </summary>
    /// <remarks>
    ///     Transient error - may succeed on retry with longer timeout.
    /// </remarks>
    public sealed record Timeout : QueryError
    {
        /// <inheritdoc />
        public override string Code => "QUERY_TIMEOUT";

        /// <inheritdoc />
        public override int StatusCode => 504;

        /// <inheritdoc />
        public override bool IsTransient => true;

        /// <summary>
        ///     The error message.
        /// </summary>
        public required string Message { get; init; }

        /// <summary>
        ///     The timeout duration that was exceeded.
        /// </summary>
        public TimeSpan? TimeoutDuration { get; init; }
    }

    /// <summary>
    ///     Failed to connect to the database.
    /// </summary>
    /// <remarks>
    ///     Transient error - may succeed on retry when connection is restored.
    /// </remarks>
    public sealed record Connection : QueryError
    {
        /// <inheritdoc />
        public override string Code => "QUERY_CONNECTION_ERROR";

        /// <inheritdoc />
        public override int StatusCode => 503;

        /// <inheritdoc />
        public override bool IsTransient => true;

        /// <summary>
        ///     The error message.
        /// </summary>
        public required string Message { get; init; }
    }

    /// <summary>
    ///     Query parameters are invalid.
    /// </summary>
    /// <remarks>
    ///     Use for invalid page numbers, page sizes, filter values, etc.
    /// </remarks>
    public sealed record InvalidParameters : QueryError
    {
        /// <inheritdoc />
        public override string Code => "QUERY_INVALID_PARAMETERS";

        /// <inheritdoc />
        public override int StatusCode => 400;

        /// <summary>
        ///     The error message describing the invalid parameters.
        /// </summary>
        public required string Message { get; init; }

        /// <summary>
        ///     The names of the invalid parameters, if known.
        /// </summary>
        public IReadOnlyList<string>? ParameterNames { get; init; }
    }

    /// <summary>
    ///     Unauthorized to execute this query.
    /// </summary>
    /// <remarks>
    ///     Use when tenant/permission filters prevent data access.
    /// </remarks>
    public sealed record Unauthorized : QueryError
    {
        /// <inheritdoc />
        public override string Code => "QUERY_UNAUTHORIZED";

        /// <inheritdoc />
        public override int StatusCode => 403;

        /// <summary>
        ///     The reason for unauthorized access.
        /// </summary>
        public string? Reason { get; init; }
    }
}
