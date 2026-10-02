namespace Pragmatic.Result.Http;

/// <summary>
///     Represents an error from an external service dependency.
///     Maps to HTTP 502 Bad Gateway, 503 Service Unavailable, or 504 Gateway Timeout.
/// </summary>
/// <remarks>
///     <para>
///         Use this when an external service (API, database, message queue) fails.
///         This error is marked as transient, allowing retry logic to attempt recovery.
///     </para>
///     <para>
///         <b>Status codes:</b>
///         <list type="bullet">
///             <item>502 - External service returned invalid response</item>
///             <item>503 - External service is unavailable</item>
///             <item>504 - External service timed out</item>
///         </list>
///     </para>
/// </remarks>
public sealed record DependencyError : Error
{
    /// <summary>Creates a <see cref="DependencyError"/>.</summary>
    public DependencyError() : this(503)
    {
    }

    /// <summary>
    ///     Creates a DependencyError with the specified status code.
    /// </summary>
    private DependencyError(int statusCode)
    {
        StatusCode = statusCode;
    }

    /// <inheritdoc />
    public override string Code => "DEPENDENCY_ERROR";

    /// <inheritdoc />
    public override int StatusCode { get; }

    /// <inheritdoc />
    public override string Title => StatusCode switch
    {
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Dependency Error"
    };

    /// <summary>
    ///     Gets the name of the service that failed.
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>
    ///     Gets the reason for the failure.
    /// </summary>
    public string? Reason { get; init; }

    /// <inheritdoc />
    public override bool IsTransient => true;

    /// <inheritdoc />
    public override TimeSpan? RetryAfter { get; init; }

    /// <inheritdoc />
    public override string MessageKey => Reason switch
    {
        "Unavailable" => "error.dependency.unavailable",
        "Timeout" => "error.dependency.timeout",
        "InvalidResponse" => "error.dependency.invalid_response",
        _ => base.MessageKey
    };

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object>? Parameters => BuildParameters();

    private IReadOnlyDictionary<string, object>? BuildParameters()
    {
        var dict = new Dictionary<string, object>();
        if (ServiceName is not null)
            dict["service"] = ServiceName;
        if (Reason is not null)
            dict["reason"] = Reason;
        return dict.Count > 0 ? dict : null;
    }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (ServiceName is not null) extensions["serviceName"] = ServiceName;
        if (Reason is not null) extensions["reason"] = Reason;
    }

    /// <summary>
    ///     Creates a DependencyError for an unavailable service (503).
    /// </summary>
    public static DependencyError Unavailable(string serviceName, TimeSpan? retryAfter = null)
    {
        return new DependencyError(503) { ServiceName = serviceName, Reason = "Unavailable", RetryAfter = retryAfter };
    }

    /// <summary>
    ///     Creates a DependencyError for a service timeout (504).
    /// </summary>
    public static DependencyError Timeout(string serviceName)
    {
        return new DependencyError(504) { ServiceName = serviceName, Reason = "Timeout" };
    }

    /// <summary>
    ///     Creates a DependencyError for an invalid response from a service (502).
    /// </summary>
    public static DependencyError InvalidResponse(string serviceName)
    {
        return new DependencyError(502) { ServiceName = serviceName, Reason = "InvalidResponse" };
    }
}