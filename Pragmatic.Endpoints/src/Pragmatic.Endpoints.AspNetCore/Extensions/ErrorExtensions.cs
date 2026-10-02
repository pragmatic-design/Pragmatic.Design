using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pragmatic.Result;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Endpoints.Extensions;

/// <summary>
///     Extension methods for converting errors to RFC 7807 Problem Details HTTP results.
/// </summary>
public static class ErrorExtensions
{
    /// <param name="error">The error to convert.</param>
    extension(IError error)
    {
        /// <summary>
        ///     Converts an error to a ProblemDetails object (RFC 7807).
        /// </summary>
        /// <returns>A ProblemDetails object representing the error.</returns>
        public ProblemDetails ToProblemDetails() => error.ToProblemDetails(resolver: null);

        /// <summary>
        ///     Converts an error to a ProblemDetails object (RFC 7807), localized when a resolver is
        ///     supplied.
        /// </summary>
        /// <param name="resolver">
        ///     Consulted for the title and the detail, by error code. Null — or a resolver that
        ///     answers null — leaves the error's own wording.
        /// </param>
        /// <returns>A ProblemDetails object representing the error.</returns>
        public ProblemDetails ToProblemDetails(IErrorMessageResolver? resolver)
        {
            var localizedTitle = resolver?.ResolveTitle(error.Code, error);
            var localizedDetail = resolver?.Resolve(error.Code, error);

            var problemDetails = new ProblemDetails
            {
                Status = error.StatusCode,
                Title = !string.IsNullOrEmpty(localizedTitle) ? localizedTitle
                    : string.IsNullOrEmpty(error.Title) ? GetTitle(error.StatusCode) : error.Title,
                Detail = !string.IsNullOrEmpty(localizedDetail) ? localizedDetail : error.Description,
                Type = ErrorExtensions.GetType(error.StatusCode)
            };

            // Error code as extension (RFC 7807 allows extensions)
            problemDetails.Extensions["code"] = error.Code;

            // Serialize custom error properties as ProblemDetails extensions
            // SG generates errors with public properties — these are domain-specific context
            SerializeErrorExtensions(error, problemDetails, resolver);

            return problemDetails;
        }

        /// <summary>
        ///     Converts an error to an IResult with appropriate status code and RFC 7807 ProblemDetails body.
        /// </summary>
        /// <returns>An IResult representing the error response.</returns>
        public IResult ToResult() => error.ToResult(httpContext: null);

        /// <summary>
        ///     Converts an error to an IResult, localizing it through the
        ///     <see cref="IErrorMessageResolver" /> the request's services provide, if any.
        /// </summary>
        /// <param name="httpContext">
        ///     The current request, for its service provider. Null keeps the error's own wording —
        ///     there is nowhere to look a resolver up.
        /// </param>
        /// <returns>An IResult representing the error response.</returns>
        /// <remarks>
        ///     Resolved per request rather than injected because this is an extension on
        ///     <see cref="IError" />, reached from generated handlers that hold an
        ///     <see cref="HttpContext" /> and no container of their own. Before this overload existed
        ///     nothing on the Pragmatic.Endpoints path could reach a resolver at all, so registering
        ///     one localized nothing (I18N-B-001).
        /// </remarks>
        public IResult ToResult(HttpContext? httpContext)
        {
            var resolver = httpContext?.RequestServices?.GetService(typeof(IErrorMessageResolver))
                as IErrorMessageResolver;
            var problemDetails = error.ToProblemDetails(resolver);

            // One typed result for every status, instead of a switch over BadRequest/NotFound/Conflict/…
            // that all produced the same body. Those overloads take `object?`, which drops the static
            // type: under AOT the serializer then has nothing to work from and the error path — the one
            // that runs when something has ALREADY gone wrong — is the thing that throws.
            //
            // application/problem+json for every status (RFC 9457): it is what the generated OpenAPI
            // document declares for error responses, and what a binding failure already sends.
            return httpContext is null
                ? Results.Problem(problemDetails)
                : Results.Json(
                    problemDetails,
                    Binding.ProblemDetailsJson.TypeInfo(httpContext),
                    contentType: "application/problem+json",
                    statusCode: error.StatusCode);
        }
    }

    private static readonly HashSet<string> BasePropertyNames = new(StringComparer.Ordinal)
    {
        "Code", "StatusCode", "Title", "Description", "MessageKey", "Parameters",
        "IsTransient", "RetryAfter", "TitleKey", "DescriptionKey", "EqualityContract"
    };

    /// <summary>
    ///     Serializes custom (non-base) public properties from error types as ProblemDetails extensions.
    ///     Uses SG-generated <see cref="Error.WriteExtensions" /> — zero reflection.
    /// </summary>
    private static void SerializeErrorExtensions(
        IError error, ProblemDetails problemDetails, IErrorMessageResolver? resolver)
    {
        // Every IError, not just the ones deriving from Error: an `is Error` narrowing would silently
        // exclude struct-based errors — ValidationError above all, whose issues carry the field name
        // the client needs. The resolver goes with it: the issues' messages are words too, and are
        // localized like the title and the detail.
        error.WriteExtensions(problemDetails.Extensions, resolver);
    }

    private static string GetTitle(int statusCode)
    {
        return statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            422 => "Unprocessable Entity",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => "Error"
        };
    }

    private static string GetType(int statusCode)
    {
        return $"https://httpstatuses.io/{statusCode}";
    }
}