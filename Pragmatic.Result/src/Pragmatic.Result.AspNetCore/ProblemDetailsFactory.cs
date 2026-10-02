using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Factory for creating ProblemDetails from Result errors.
/// </summary>
/// <remarks>
///     <para>
///         This factory converts <see cref="Error" /> instances
///         to RFC 7807 Problem Details format for HTTP API responses.
///     </para>
///     <para>
///         For localization, register an <see cref="IErrorMessageResolver" /> implementation.
///     </para>
/// </remarks>
public static class ProblemDetailsFactory
{
    /// <summary>
    ///     Creates a ProblemDetails from an IError.
    /// </summary>
    /// <param name="error">The error (uses error.StatusCode for HTTP status)</param>
    /// <param name="instance">Optional request path for Problem Details instance field</param>
    /// <returns>A ProblemDetails instance</returns>
    public static ProblemDetails Create(IError error, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var problemDetails = new ProblemDetails
        {
            Status = error.StatusCode,
            Title = string.IsNullOrEmpty(error.Title) ? GetDefaultTitle(error.StatusCode) : error.Title,
            Type = GetProblemType(error.StatusCode),
            Instance = instance,
            // Non-localized fallback path: surface the error's own Description as the human-readable detail.
            Detail = error.Description
        };

        // Add error code as extension
        problemDetails.Extensions["code"] = error.Code;

        // Every IError, not just the ones deriving from Error: behind `is Error` a ValidationError
        // reached this path naming no field, as it once did the other mapper.
        error.WriteExtensions(problemDetails.Extensions);

        // Set retry header AFTER WriteExtensions so the typed int retryAfter is authoritative and is never
        // clobbered by a custom WriteExtensions override (aligns with DefaultProblemDetailsFactory).
        if (error is Error { IsTransient: true, RetryAfter: not null } transientError)
            problemDetails.Extensions["retryAfter"] = (int)transientError.RetryAfter.Value.TotalSeconds;

        return problemDetails;
    }

    /// <summary>
    ///     Gets the default RFC title for the given HTTP status code.
    /// </summary>
    internal static string GetDefaultTitle(int statusCode)
    {
        return statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            422 => "Unprocessable Entity",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => "Error"
        };
    }

    /// <summary>
    ///     Gets the problem type URI for the given HTTP status code.
    /// </summary>
    internal static string GetProblemType(int statusCode)
    {
        return $"https://httpstatuses.io/{statusCode}";
    }
}