using System.Net;
using System.Net.ServerSentEvents;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     W4 endpoint gap: SSE streaming — incremental data: events, pre-stream failures as
///     ProblemDetails (404 before the stream opens), mid-stream failures as a terminal
///     event: error, and StreamingDomainAction through the invoker pipeline.
/// </summary>
public class SseStreamingTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task StreamingEndpoint_StreamsItemsAsSse()
    {
        var (response, events) = await ReadSseAsync("/api/reservations-feed?count=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        events.Should().HaveCount(3);
        events.Should().OnlyContain(e => e.EventType == "message");
        events[0].Data.Should().Contain("reservation-event-1");
        events[2].Data.Should().Contain("reservation-event-3");
    }

    [Fact]
    public async Task StreamingEndpoint_PreStreamFailure_ReturnsProblemDetails()
    {
        var response = await GetRawAsync("/api/reservations-feed?failFirst=true");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a failure yielded FIRST must become a normal HTTP error (first-item peek)");
        response.Content.Headers.ContentType!.MediaType.Should().NotBe("text/event-stream",
            "the stream must never open on a pre-stream failure");
    }

    [Fact]
    public async Task StreamingEndpoint_MidStreamFailure_EmitsErrorEvent()
    {
        var (response, events) = await ReadSseAsync("/api/reservations-feed?failMidStream=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the stream already opened");
        events.Should().HaveCount(2);
        events[0].EventType.Should().Be("message");
        events[1].EventType.Should().Be("error");
        events[1].Data.Should().Contain("404");
    }

    [Fact]
    public async Task StreamingDomainAction_StreamsThroughInvokerPipeline()
    {
        var (response, events) = await ReadSseAsync("/api/availability-scan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        events.Should().HaveCount(3);
        events[0].Data.Should().Contain("slot-9:00");
        events[2].Data.Should().Contain("slot-11:00");
    }

    /// <summary>
    ///     <c>[Sse(HeartbeatSeconds = 1)]</c>: an idle stream is kept alive.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The feed's normal state is idle — a front-desk screen holds it open and the
    ///         interesting moments are minutes apart — and an idle connection is what a proxy
    ///         closes. The keep-alive is an SSE comment, <c>: hb</c>, which a parser skips; only the
    ///         socket notices.
    ///     </para>
    ///     <para>
    ///         ⚠️ Read from the <b>raw</b> stream, because <see cref="SseParser" /> discards
    ///         comments: a test written on the parsed events cannot see a heartbeat at all, and
    ///         would pass with the declaration removed.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Counted, not looked for.</b> The keep-alive has to repeat for as long as the gap
    ///         lasts. A loop that wrote one <c>: hb</c> and then waited on
    ///         <c>Timeout.InfiniteTimeSpan</c> for the same <c>MoveNextAsync</c> would leave a stream
    ///         idle for a minute silent again after the first second, and the proxy would close it
    ///         anyway. An assertion that the comment is *present* passes on both behaviours.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AnIdleStream_IsKeptAliveForAsLongAsItWaits()
    {
        var raw = await ReadRawAsync("/api/reservations-feed?count=2&quietSeconds=3");

        var beats = raw.Split(": hb", StringSplitOptions.None).Length - 1;

        beats.Should().BeGreaterThan(1,
            "three seconds of silence at one second an interval is more than one keep-alive — "
            + $"the stream held {beats}");
        raw.Should().Contain("reservation-event-2", "and the next item still arrives after the wait");
    }

    /// <summary>
    ///     The control: a stream that is never idle sends no keep-alive.
    /// </summary>
    /// <remarks>
    ///     Without it, "the raw stream contains <c>: hb</c>" is satisfied by a heartbeat written
    ///     unconditionally — which would put a comment between every pair of events and say nothing
    ///     about idleness.
    /// </remarks>
    [Fact]
    public async Task AStreamThatIsNeverIdle_SendsNoKeepAlive()
    {
        var raw = await ReadRawAsync("/api/reservations-feed?count=3");

        raw.Should().NotContain(": hb", "three items ten milliseconds apart leave no second of silence");
    }

    private async Task<string> ReadRawAsync(string url)
    {
        var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<(HttpResponseMessage Response, List<SseItem<string>> Events)> ReadSseAsync(string url)
    {
        var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var events = new List<SseItem<string>>();
        await foreach (var item in SseParser.Create(stream).EnumerateAsync())
            events.Add(item);

        return (response, events);
    }
}
