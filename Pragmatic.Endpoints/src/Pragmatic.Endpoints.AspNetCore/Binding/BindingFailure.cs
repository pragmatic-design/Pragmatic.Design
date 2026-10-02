using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pragmatic.Endpoints.Binding;

/// <summary>
///     Writes the 400 a generated endpoint answers with when a request value is missing or malformed.
/// </summary>
/// <remarks>
///     A bare status code is not an answer: the caller learns that something was wrong and nothing
///     about what. ASP.NET's own binder writes a body, so the generated binding writes one too.
/// </remarks>
public static class BindingFailure
{
    /// <summary>Writes an RFC 7807 problem naming the parameter that could not be bound.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="parameter">The parameter name, as the caller would spell it.</param>
    /// <param name="reason">What was wrong with it.</param>
    public static Task WriteAsync(HttpContext context, string parameter, string reason)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = $"The request could not be bound: {reason}.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        };

        // Shaped like a validation failure, because that is what it is: the field name is the part
        // a client can act on.
        problem.Extensions["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [parameter] = [reason],
        };

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(problem, ProblemDetailsJson.TypeInfo(context), "application/problem+json");
    }

    /// <summary>
    ///     Writes the 413 a generated endpoint answers with when the body goes past a limit it declared.
    /// </summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="reason">What the reader said, verbatim.</param>
    /// <remarks>
    ///     <para>
    ///         ⚠️ This exists for the same reason as the 415 that follows: the alternative was a <b>500</b>.
    ///         <c>ReadFormAsync</c> throws <c>InvalidDataException</c> when a multipart body exceeds
    ///         <c>RequestFormLimits.MultipartBodyLengthLimit</c>, nobody caught it, and an upload one
    ///         byte over a ceiling the endpoint had declared itself answered with an unhandled server
    ///         error.
    ///     </para>
    ///     <para>
    ///         ⚠️ It also made a generated check unreachable: <c>[HasAttachments]</c> emits the form
    ///         limit <b>and</b> a <c>file.Length &gt; max</c> branch answering 413, and the branch could
    ///         never run, because the read that has to precede it threw first.
    ///     </para>
    ///     <para>
    ///         413 rather than 400: the request is well formed and the server declined its size, which is
    ///         the status RFC 9110 has for exactly that.
    ///     </para>
    /// </remarks>
    public static Task WritePayloadTooLargeAsync(HttpContext context, string reason)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "Payload Too Large",
            Detail = $"The request body exceeds a limit this endpoint declares: {reason}",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        };

        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(problem, ProblemDetailsJson.TypeInfo(context), "application/problem+json");
    }

    /// <summary>
    ///     Writes the 415 a generated endpoint answers with when the body is not JSON it can read.
    /// </summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="reason">What the reader said, verbatim.</param>
    /// <remarks>
    ///     <para>
    ///         ⚠️ This exists because the alternative was a <b>500</b>. The generated delegate guarded
    ///         <c>ReadFromJsonAsync</c> with <c>catch (JsonException)</c>, which is the wrong exception
    ///         for the commonest mistake there is: a request with a perfectly good JSON payload and no
    ///         <c>Content-Type</c> header throws <c>InvalidOperationException</c>, nobody caught it, and
    ///         the caller got an unhandled server error naming an internal helper. It reached every write
    ///         endpoint of every application built on the framework.
    ///     </para>
    ///     <para>
    ///         415 rather than 400 because the request is not malformed — the server declined the media
    ///         type, or its absence, and RFC 9110 has a status that says exactly that.
    ///     </para>
    /// </remarks>
    public static Task WriteUnsupportedMediaTypeAsync(HttpContext context, string reason)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status415UnsupportedMediaType,
            Title = "Unsupported Media Type",
            Detail = $"The request body could not be read as JSON: {reason}.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        };

        context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(problem, ProblemDetailsJson.TypeInfo(context), "application/problem+json");
    }
}
