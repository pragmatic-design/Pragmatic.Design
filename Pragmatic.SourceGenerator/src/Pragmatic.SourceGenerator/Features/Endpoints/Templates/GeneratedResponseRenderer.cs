using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     The result a handler answers a JSON body with, when the endpoint has a generated response writer.
/// </summary>
/// <remarks>
///     Null when it has none, and the template keeps the <c>Results</c> call it always wrote. The generated result
///     sets the same status and <c>Location</c>, and falls back to that same call where the host's options are not
///     the ones the writer reproduces.
/// </remarks>
internal static class GeneratedResponseRenderer
{
    private const string Response = "global::Pragmatic.Endpoints.Responses.GeneratedJsonResponse";

    /// <param name="model">The endpoint.</param>
    /// <param name="body">The expression of the value answered with.</param>
    /// <param name="statusCode">The success status.</param>
    /// <param name="location">The expression of the <c>Location</c> of a <c>201</c>, or null.</param>
    public static string? Render(EndpointModel model, string body, int statusCode, string? location = null)
        => model is { ResponseWriter: { } plan, ResponseWriterDelegate: { } write, ResponseWriterShape: { } shape }
            ? $"(Microsoft.AspNetCore.Http.IResult)new {Response}<{plan.TypeExpr}>({body}!, {statusCode}, {location ?? "null"}, {write}, {shape})"
            : null;
}
