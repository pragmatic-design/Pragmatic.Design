namespace Pragmatic.Client;

/// <summary>
///     Generic API error deserialized from a ProblemDetails (RFC 7807) response.
///     Field mapping: <c>Code</c> ← <c>code</c>, <c>Title</c> ← <c>title</c>,
///     <c>Detail</c> ← <c>detail</c>, <c>StatusCode</c> ← HTTP status code.
///     The source generator extends this with typed error records that carry
///     strongly-typed extension properties (e.g., <c>EntityId</c> for a NotFoundError).
///     Use <c>StatusCode = 0</c> as the "unset/unknown" sentinel; 500 is reserved for
///     confirmed server errors.
/// </summary>
public record ApiError : Pragmatic.Result.IError
{
    /// <summary>Application-level error code from ProblemDetails <c>code</c> field. Defaults to "UNKNOWN".</summary>
    public string Code { get; init; } = "UNKNOWN";

    /// <summary>
    ///     HTTP status code from the response. A value of 0 indicates the status code
    ///     was not determined (e.g., parse failure before the response was read).
    /// </summary>
    public int StatusCode { get; init; } = 0;

    /// <summary>Human-readable summary from ProblemDetails <c>title</c> field.</summary>
    public string Title { get; init; } = "Unknown Error";

    /// <summary>Optional detailed explanation from ProblemDetails <c>detail</c> field, or JSON parse error message.</summary>
    public string? Detail { get; init; }

    /// <summary>
    ///     Additional extension values from ProblemDetails (RFC 7807).
    ///     Contains domain-specific context that isn't mapped to a typed error property.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}
