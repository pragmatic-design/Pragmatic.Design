using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Endpoints.Responses;
using Pragmatic.Endpoints.Serialization;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Sse;

/// <summary>
///     Writes an SSE stream: one <c>data:</c> event per item (flushed immediately —
///     backpressure is the awaited flush), an optional idle keep-alive comment, and a
///     terminal <c>event: error</c> when the stream yields a failure mid-stream.
/// </summary>
/// <typeparam name="TItem">The stream item type.</typeparam>
public sealed class SseStreamResult<TItem>(
    IAsyncEnumerator<SseStreamEvent<TItem>> enumerator,
    SseStreamEvent<TItem>? first,
    SseStreamOptions? options) : IResult
{
    private static readonly byte[] Heartbeat = ": hb\n\n"u8.ToArray();

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        // Disable proxy buffering (nginx) — SSE must reach the client per event.
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var jsonOptions = httpContext.RequestServices
            .GetService(typeof(IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>))
            is IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> hostJson
            ? hostJson.Value.SerializerOptions
            : null;

        var heartbeatSeconds = options?.HeartbeatSeconds is > 0 ? options.HeartbeatSeconds.Value : 0;
        var ct = httpContext.RequestAborted;

        await using var _ = enumerator.ConfigureAwait(false);
        try
        {
            if (first is not { } current)
            {
                // Empty stream: 200 with zero events.
                await response.Body.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            while (true)
            {
                if (current.IsError)
                {
                    await WriteErrorAsync(response, current.Error!, ct).ConfigureAwait(false);
                    return; // terminal — dispose runs the iterator's finally blocks
                }

                await WriteItemAsync(response, current.Item!, jsonOptions, ct).ConfigureAwait(false);

                if (!await MoveNextWithHeartbeatAsync(response, heartbeatSeconds, ct).ConfigureAwait(false))
                    return;

                current = enumerator.Current;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — nothing to write.
        }
        catch (Exception)
        {
            // Result-over-exceptions: handlers should yield failures. An escaped exception
            // cannot change the status (already 200) — emit a terminal error event.
            await WriteErrorAsync(
                response,
                new SseInternalError(),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<bool> MoveNextWithHeartbeatAsync(
        HttpResponse response, int heartbeatSeconds, CancellationToken ct)
    {
        if (heartbeatSeconds <= 0)
            return await enumerator.MoveNextAsync().ConfigureAwait(false);

        // One MoveNextAsync, awaited across as many heartbeat intervals as the gap lasts.
        // ⚠️ Writing one keep-alive and then waiting on Timeout.InfiniteTimeSpan for the same
        // MoveNextAsync would give a gap of any length exactly ONE heartbeat: an idle feed would go
        // quiet again after the first interval, and a proxy closing on 30s of silence would still
        // close it.
        var moveNext = enumerator.MoveNextAsync().AsTask();

        while (true)
        {
            var completed = await Task.WhenAny(
                moveNext, Task.Delay(TimeSpan.FromSeconds(heartbeatSeconds), ct)).ConfigureAwait(false);

            if (completed == moveNext)
                return await moveNext.ConfigureAwait(false);

            await response.Body.WriteAsync(Heartbeat, ct).ConfigureAwait(false);
            await response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private static async ValueTask WriteItemAsync(
        HttpResponse response, TItem item, JsonSerializerOptions? jsonOptions, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(item, jsonOptions);
        var payload = Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await response.Body.WriteAsync(payload, ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async ValueTask WriteErrorAsync(HttpResponse response, IError error, CancellationToken ct)
    {
        var sseError = new SseError(
            error.Code,
            error.Title,
            error.Description,
            error.StatusCode);
        var json = JsonSerializer.Serialize(sseError, PragmaticEndpointsJsonContext.Default.SseError);
        var payload = Encoding.UTF8.GetBytes($"event: error\ndata: {json}\n\n");
        await response.Body.WriteAsync(payload, ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Fallback error for exceptions escaping the iterator after the stream opened.</summary>
    private sealed record SseInternalError : IError
    {
        public string Code => "internal";
        public string Title => "Internal Server Error";
        public string? Description => "The stream terminated unexpectedly.";
        public int StatusCode => 500;
    }
}
