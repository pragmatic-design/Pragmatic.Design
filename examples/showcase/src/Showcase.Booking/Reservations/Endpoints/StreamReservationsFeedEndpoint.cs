using System.Runtime.CompilerServices;

namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Streams a short reservations feed.
/// Demonstrates: StreamingEndpoint (SSE) — items as data: events, mid-stream failures as a
/// terminal event: error, pre-stream failures as ProblemDetails (first-item peek).
/// </summary>
/// <remarks>
/// ⚠️ <c>[Sse(HeartbeatSeconds = 1)]</c> is what keeps an idle feed open. A front-desk screen holds
/// this connection all day and the interesting moments are minutes apart; a proxy closes a
/// connection that has been silent, and the client then reconnects into whatever it missed. The
/// keep-alive is a comment line — <c>: hb</c> — so it costs a consumer nothing: an SSE parser skips
/// it, and only the socket notices.
/// </remarks>
[Endpoint(HttpVerb.Get, "/api/reservations-feed")]
[ApiSummary("Stream reservations feed")]
[ApiTags("Reservations")]
[Sse(HeartbeatSeconds = 1)]
public partial class StreamReservationsFeedEndpoint : StreamingEndpoint<string, NotFoundError>
{
    /// <summary>Number of items to stream before completing.</summary>
    [FromQuery]
    public int Count { get; set; } = 3;

    /// <summary>
    ///     Seconds of silence <b>after</b> the first item — demo/test hook, like the two failures below.
    /// </summary>
    /// <remarks>
    ///     An idle feed is the normal state of this endpoint and the only state in which the
    ///     heartbeat does anything, so it has to be reproducible on demand.
    ///     ⚠️ After the first item and not before it: the first item is peeked before the response
    ///     opens (that is how a pre-stream failure can still be a 404), so nothing can be written
    ///     during a wait that precedes it — a heartbeat included.
    /// </remarks>
    [FromQuery]
    public int QuietSeconds { get; set; }

    /// <summary>Fail before the stream opens (pre-stream 404) — demo/test hook.</summary>
    [FromQuery]
    public bool FailFirst { get; set; }

    /// <summary>Fail after the first item (mid-stream error event) — demo/test hook.</summary>
    [FromQuery]
    public bool FailMidStream { get; set; }

    public override async IAsyncEnumerable<Result<string, NotFoundError>> HandleAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (FailFirst)
        {
            yield return NotFoundError.For<string>("ReservationFeed", "unavailable");
            yield break;
        }

        for (var i = 1; i <= Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return $"reservation-event-{i}";

            if (QuietSeconds > 0 && i == 1)
                await Task.Delay(TimeSpan.FromSeconds(QuietSeconds), ct).ConfigureAwait(false);

            if (FailMidStream && i == 1)
            {
                yield return NotFoundError.For<string>("ReservationFeed", "interrupted");
                yield break;
            }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }
}
