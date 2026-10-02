using Microsoft.AspNetCore.Http;

namespace Pragmatic.Endpoints.Idempotency;

/// <summary>
///     Writes a captured <see cref="IdempotentResponse" /> to the HTTP response —
///     used both for the first execution (whose body was captured to a buffer)
///     and for replayed retries.
/// </summary>
public sealed class IdempotentReplayResult(IdempotentResponse response) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = response.StatusCode;

        if (response.ContentType is not null)
            httpContext.Response.ContentType = response.ContentType;

        if (response.Headers is not null)
            foreach (var header in response.Headers)
                httpContext.Response.Headers[header.Key] = header.Value;

        if (response.Body.Length > 0)
            await httpContext.Response.Body
                .WriteAsync(response.Body, httpContext.RequestAborted)
                .ConfigureAwait(false);
    }
}
