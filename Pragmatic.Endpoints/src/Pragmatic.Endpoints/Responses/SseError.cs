namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Payload of the SSE <c>error</c> event emitted when a streaming endpoint yields a
///     failure after the stream has started (the HTTP status is already 200 and cannot
///     change). Shape mirrors ProblemDetails; fixed → AOT-safe via a dedicated context.
/// </summary>
/// <param name="Code">Machine-readable error code (IError.Code).</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="Detail">Optional detail message.</param>
/// <param name="Status">HTTP-equivalent status code of the error.</param>
public sealed record SseError(string Code, string Title, string? Detail, int Status);
