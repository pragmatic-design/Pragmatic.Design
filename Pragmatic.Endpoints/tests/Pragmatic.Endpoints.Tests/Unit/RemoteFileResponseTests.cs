using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Responses;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     Guards the client half of a file download: the bytes must still be readable after the call that
///     produced the <see cref="FileResponse" /> has returned, and the response must be released exactly
///     once, when the caller disposes the file.
/// </summary>
public class RemoteFileResponseTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");

    private sealed class TrackingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(response);
    }

    /// <summary>Reports whether it was disposed — the only way to observe premature release.</summary>
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static HttpResponseMessage BuildResponse(
        TrackingStream body,
        string contentType = "application/pdf",
        string? fileName = "report.pdf")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (fileName is not null)
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = fileName
            };

        return response;
    }

    /// <summary>
    ///     Mirrors the generated remote invoker: issue the request, hand the response to
    ///     <see cref="RemoteFileResponse" />, return. Nothing here disposes the response.
    /// </summary>
    private static async Task<FileResponse> InvokeAsync(HttpResponseMessage response)
    {
        using var client = new HttpClient(new TrackingHandler(response)) { BaseAddress = new Uri("http://remote.invalid") };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_pragmatic/invoke");

        var received = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);

        return await RemoteFileResponse.ReadAsync(received, ct: CancellationToken.None).ConfigureAwait(false);
    }

    [Fact]
    public async Task ReadAsync_BytesAreReadableAfterTheInvokerReturned()
    {
        // The whole point: a `using` on the HttpResponseMessage inside the invoker would leave this
        // read returning zero bytes (or throwing ObjectDisposedException) far from its cause.
        var body = new TrackingStream(Payload);

        var file = await InvokeAsync(BuildResponse(body)).ConfigureAwait(true);

        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, CancellationToken.None).ConfigureAwait(true);

        buffer.ToArray().Should().Equal(Payload);
    }

    [Fact]
    public async Task ReadAsync_DoesNotDisposeTheResponseBeforeTheCallerReadsIt()
    {
        var body = new TrackingStream(Payload);

        var file = await InvokeAsync(BuildResponse(body)).ConfigureAwait(true);

        body.Disposed.Should().BeFalse("the response must stay alive until the FileResponse is disposed");
        file.Content.Dispose();
    }

    [Fact]
    public async Task ReadAsync_DisposingTheFileResponseReleasesTheHttpResponse()
    {
        var body = new TrackingStream(Payload);

        var file = await InvokeAsync(BuildResponse(body)).ConfigureAwait(true);
        file.Dispose();

        body.Disposed.Should().BeTrue("ownership of the response follows the stream");
    }

    [Fact]
    public async Task ReadAsync_TakesContentTypeFromTheResponseHeaders()
    {
        var file = await InvokeAsync(BuildResponse(new TrackingStream(Payload), "image/png")).ConfigureAwait(true);

        file.ContentType.Should().Be("image/png");
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_TakesFileNameFromContentDisposition()
    {
        var file = await InvokeAsync(BuildResponse(new TrackingStream(Payload), fileName: "invoice-2026.pdf")).ConfigureAwait(true);

        file.FileName.Should().Be("invoice-2026.pdf");
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_WithoutContentDisposition_UsesTheDefaultFileName()
    {
        var file = await InvokeAsync(BuildResponse(new TrackingStream(Payload), fileName: null)).ConfigureAwait(true);

        file.FileName.Should().Be(RemoteFileResponse.DefaultFileName);
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_WithoutContentType_FallsBackToOctetStream()
    {
        var body = new TrackingStream(Payload);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
        response.Content.Headers.ContentType = null;

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.ContentType.Should().Be(RemoteFileResponse.DefaultContentType);
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_PropagatesETagWithoutTheTransportQuotes()
    {
        // FileResponseExtensions re-adds the quotes on the way out; keeping them would double them.
        var response = BuildResponse(new TrackingStream(Payload));
        response.Headers.ETag = new EntityTagHeaderValue("\"abc123\"");

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.ETag.Should().Be("abc123");
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_PropagatesLastModified()
    {
        var modified = new DateTimeOffset(2026, 7, 25, 10, 30, 0, TimeSpan.Zero);
        var response = BuildResponse(new TrackingStream(Payload));
        response.Content.Headers.LastModified = modified;

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.LastModified.Should().Be(modified);
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_PrefersFileNameStarOverFileName()
    {
        var response = BuildResponse(new TrackingStream(Payload));
        response.Content.Headers.ContentDisposition!.FileNameStar = "relazione-annuale.pdf";

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.FileName.Should().Be("relazione-annuale.pdf");
        file.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesTheHttpResponse()
    {
        var body = new TrackingStream(Payload);

        var file = await InvokeAsync(BuildResponse(body)).ConfigureAwait(true);
        await file.Content.DisposeAsync().ConfigureAwait(true);

        body.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var file = await InvokeAsync(BuildResponse(new TrackingStream(Payload))).ConfigureAwait(true);

        file.Dispose();
        var act = () => file.Dispose();

        act.Should().NotThrow();
    }

    // ── Out-of-band metadata: what Content-Disposition cannot carry ──

    [Fact]
    public async Task ReadAsync_InlineFile_RecoversBothTheFlagAndTheName()
    {
        // The case Content-Disposition cannot express: an inline file emits no such header at all, so
        // without a channel of their own both the flag and the name are simply gone past the hop.
        var response = BuildResponse(new TrackingStream(Payload), fileName: null);
        response.Headers.TryAddWithoutValidation(
            PragmaticFileHeaders.FileName, PragmaticFileHeaders.EncodeFileName("preventivo €.pdf"));
        response.Headers.TryAddWithoutValidation(PragmaticFileHeaders.Inline, "true");

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.Inline.Should().BeTrue();
        file.FileName.Should().Be("preventivo €.pdf");
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_WithoutTheMetadataHeaders_StillReadsContentDisposition()
    {
        // A peer that does not speak the extra headers — a plain file endpoint — must keep working.
        var file = await InvokeAsync(BuildResponse(new TrackingStream(Payload), fileName: "legacy.pdf"))
            .ConfigureAwait(true);

        file.FileName.Should().Be("legacy.pdf");
        file.Inline.Should().BeFalse();
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_MetadataHeaderWinsOverContentDisposition()
    {
        var response = BuildResponse(new TrackingStream(Payload), fileName: "mangled.pdf");
        response.Headers.TryAddWithoutValidation(
            PragmaticFileHeaders.FileName, PragmaticFileHeaders.EncodeFileName("réel.pdf"));

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.FileName.Should().Be("réel.pdf");
        file.Dispose();
    }

    // ── Range: what the response already contains ──

    [Fact]
    public async Task ReadAsync_WholeFile_IsNotMarkedPartialAndDoesNotReopenRangeProcessing()
    {
        var file = await InvokeAsync(BuildResponse(new TrackingStream(Payload))).ConfigureAwait(true);

        file.PartialContent.Should().BeNull();
        file.EnableRangeProcessing.Should().BeFalse(
            "the remote end already owns range semantics — re-enabling it here would let the calling " +
            "host apply the caller's Range a second time, to content that may already be an excerpt");
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_PartialContent_CarriesTheRangeBack()
    {
        var response = BuildResponse(new TrackingStream(Payload));
        response.StatusCode = HttpStatusCode.PartialContent;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(100, 199, 5000);

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.PartialContent.Should().Be(new FileContentRange(100, 199, 5000));
        file.PartialContent!.Length.Should().Be(100);
        file.Dispose();
    }

    [Fact]
    public async Task ReadAsync_PartialContentWithoutContentRange_FailsLoudly()
    {
        // 206 without Content-Range violates RFC 9110. The body is then an excerpt of unknown position:
        // forwarding it as a whole file is silent corruption, so this fails instead.
        var response = BuildResponse(new TrackingStream(Payload));
        response.StatusCode = HttpStatusCode.PartialContent;

        var act = async () => await InvokeAsync(response).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Content-Range*").ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadAsync_StrayContentRangeOnA200_IsIgnored()
    {
        var response = BuildResponse(new TrackingStream(Payload));
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 9, 10);

        var file = await InvokeAsync(response).ConfigureAwait(true);

        file.PartialContent.Should().BeNull("200 is the whole representation, whatever the headers say");
        file.Dispose();
    }
}
