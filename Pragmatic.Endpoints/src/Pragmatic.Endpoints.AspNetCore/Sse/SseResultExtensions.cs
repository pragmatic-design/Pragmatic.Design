using Microsoft.AspNetCore.Http;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Endpoints.Extensions;
using Pragmatic.Endpoints.Responses;

namespace Pragmatic.Endpoints.Sse;

/// <summary>
///     Entry point used by generated SSE handlers: peeks the first stream event to decide
///     between a plain HTTP error response and an open SSE stream.
/// </summary>
public static class SseResultExtensions
{
    /// <summary>
    ///     Peeks the first event. A failure yielded FIRST becomes a normal HTTP response
    ///     (ProblemDetails with the error's status) — the response has not started yet.
    ///     Otherwise the stream opens as 200 <c>text/event-stream</c> and the buffered
    ///     first item is re-emitted.
    /// </summary>
    public static async Task<IResult> ToSseResult<TItem>(
        this IAsyncEnumerable<SseStreamEvent<TItem>> stream,
        HttpContext httpContext,
        SseStreamOptions? options = null,
        CancellationToken ct = default)
    {
        var enumerator = stream.GetAsyncEnumerator(ct);

        bool hasFirst;
        try
        {
            hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            return Results.Empty; // client already gone
        }
        catch
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (!hasFirst)
            // Legitimate empty stream: 200 with zero events (the result disposes the enumerator).
            return new SseStreamResult<TItem>(enumerator, null, options);

        var first = enumerator.Current;
        if (first.IsError)
        {
            // Pre-stream failure: the status code is still ours to set.
            await enumerator.DisposeAsync().ConfigureAwait(false);
            return ErrorExtensions.ToResult(first.Error!);
        }

        return new SseStreamResult<TItem>(enumerator, first, options);
    }
}
