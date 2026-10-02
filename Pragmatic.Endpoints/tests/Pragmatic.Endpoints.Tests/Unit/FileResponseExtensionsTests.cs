using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Extensions;
using Pragmatic.Endpoints.Responses;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class FileResponseExtensionsTests
{
    private static FileResponse Pdf(string? etag = null, DateTimeOffset? lastModified = null)
        => new(new MemoryStream([1, 2, 3]), "application/pdf", "doc.pdf")
        {
            ETag = etag,
            LastModified = lastModified
        };

    [Fact]
    public void ToResult_NoHttpContext_ReturnsFileResult()
    {
        var response = Pdf(etag: "abc");

        var result = response.ToResult();

        result.Should().NotBeNull();
        result.Should().NotBeOfType<StatusCodeHttpResult>();
    }

    [Fact]
    public void ToResult_MatchingIfNoneMatch_Returns304()
    {
        var response = Pdf(etag: "abc");
        var http = new DefaultHttpContext();
        http.Request.Headers.IfNoneMatch = "\"abc\"";

        var result = response.ToResult(http);

        result.Should().BeOfType<StatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void ToResult_WildcardIfNoneMatch_Returns304()
    {
        var response = Pdf(etag: "abc");
        var http = new DefaultHttpContext();
        http.Request.Headers.IfNoneMatch = "*";

        var result = response.ToResult(http);

        result.Should().BeOfType<StatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void ToResult_NonMatchingIfNoneMatch_DoesNotReturn304()
    {
        var response = Pdf(etag: "abc");
        var http = new DefaultHttpContext();
        http.Request.Headers.IfNoneMatch = "\"different\"";

        var result = response.ToResult(http);

        result.Should().NotBeOfType<StatusCodeHttpResult>();
    }

    [Fact]
    public void ToResult_WeakEtagAgainstStrong_DoesNotMatch_StrongComparison()
    {
        // RFC 7232: GET/HEAD 304 requires strong comparison — a weak tag must NOT match.
        var response = Pdf(etag: "abc");
        var http = new DefaultHttpContext();
        http.Request.Headers.IfNoneMatch = "W/\"abc\"";

        var result = response.ToResult(http);

        result.Should().NotBeOfType<StatusCodeHttpResult>();
    }

    [Fact]
    public void ToResult_ResponseWithoutEtag_IgnoresIfNoneMatch()
    {
        var response = Pdf(etag: null);
        var http = new DefaultHttpContext();
        http.Request.Headers.IfNoneMatch = "\"abc\"";

        var result = response.ToResult(http);

        result.Should().NotBeOfType<StatusCodeHttpResult>();
    }

    [Fact]
    public void ToResult_IfModifiedSinceAfterLastModified_Returns304()
    {
        var lastModified = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var response = Pdf(lastModified: lastModified);
        var http = new DefaultHttpContext();
        // Set via the typed header setter (a DateTimeOffset): assigning a raw HTTP-date *string*
        // to Headers["If-Modified-Since"] does NOT round-trip through GetTypedHeaders().IfModifiedSince
        // (it reads back as null, regardless of culture), which made this test fail deterministically.
        http.Request.GetTypedHeaders().IfModifiedSince = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var result = response.ToResult(http);

        result.Should().BeOfType<StatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void ToResult_IfModifiedSinceBeforeLastModified_ReturnsFile()
    {
        var lastModified = new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero);
        var response = Pdf(lastModified: lastModified);
        var http = new DefaultHttpContext();
        // Typed setter (see sibling test): a raw HTTP-date string does not round-trip here.
        http.Request.GetTypedHeaders().IfModifiedSince = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = response.ToResult(http);

        result.Should().NotBeOfType<StatusCodeHttpResult>();
    }

    // ── Out-of-band metadata: opt-in, and only on the RPC hop ──

    /// <summary>ASP.NET's own file results resolve an <c>ILoggerFactory</c> off the request services.</summary>
    private static readonly IServiceProvider Services =
        new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext NewContext()
    {
        var http = new DefaultHttpContext { RequestServices = Services };
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static async Task<DefaultHttpContext> ExecuteAsync(IResult result)
    {
        var http = NewContext();
        await result.ExecuteAsync(http).ConfigureAwait(false);
        return http;
    }

    [Fact]
    public async Task ToResult_LocalPath_EmitsNoPragmaticHeaders()
    {
        // The public endpoint's response must be byte-for-byte what it always was.
        var http = await ExecuteAsync(Pdf().ToResult()).ConfigureAwait(true);

        http.Response.Headers.Should().NotContainKey(PragmaticFileHeaders.FileName);
        http.Response.Headers.Should().NotContainKey(PragmaticFileHeaders.Inline);
    }

    [Fact]
    public async Task ToResult_RpcHop_CarriesTheNameAndInlineFlagOutOfBand()
    {
        var response = new FileResponse(new MemoryStream([1, 2, 3]), "application/pdf", "grafico €.pdf")
        {
            Inline = true
        };

        var http = await ExecuteAsync(response.ToResult(null, includeRpcMetadata: true)).ConfigureAwait(true);

        PragmaticFileHeaders.DecodeFileName(http.Response.Headers[PragmaticFileHeaders.FileName])
            .Should().Be("grafico €.pdf");
        PragmaticFileHeaders.DecodeInline(http.Response.Headers[PragmaticFileHeaders.Inline])
            .Should().BeTrue();

        // The extra headers are additional, not a substitute: an inline file still emits no
        // Content-Disposition, exactly as before.
        http.Response.Headers.Should().NotContainKey("Content-Disposition");
    }

    [Fact]
    public async Task ToResult_RpcHopAttachment_StillEmitsTheStandardContentDisposition()
    {
        var http = await ExecuteAsync(Pdf().ToResult(null, includeRpcMetadata: true)).ConfigureAwait(true);

        http.Response.Headers["Content-Disposition"].ToString().Should().Contain("attachment")
            .And.Contain("doc.pdf");
    }

    // ── Already-partial content: forwarded, never re-ranged ──

    private static FileResponse Partial(byte[] bytes, long from, long to, long? total)
        => new(new MemoryStream(bytes), "application/pdf", "doc.pdf")
        {
            PartialContent = new FileContentRange(from, to, total),
            // Set deliberately: the whole hazard is that a response can arrive marked both "already
            // partial" and "range-processable". The partial branch must win.
            EnableRangeProcessing = true
        };

    [Fact]
    public async Task ToResult_AlreadyPartial_Answers206WithTheRangeItWasGiven()
    {
        var slice = new byte[] { 10, 11, 12, 13 };

        var http = await ExecuteAsync(Partial(slice, 100, 103, 5000).ToResult()).ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes 100-103/5000");
        http.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
        http.Response.ContentLength.Should().Be(4);
    }

    [Fact]
    public async Task ToResult_AlreadyPartial_DoesNotApplyTheRequestRangeASecondTime()
    {
        // The subtle failure this whole path exists for: the content is already the excerpt, and the
        // request carries the same Range that produced it. Re-applying it yields a range of a range —
        // plausible status, plausible Content-Range, wrong bytes, and nothing throws.
        var slice = new byte[] { 10, 11, 12, 13 };
        var result = Partial(slice, 100, 103, 5000).ToResult();

        var http = NewContext();
        http.Request.Headers.Range = "bytes=100-103";
        await result.ExecuteAsync(http).ConfigureAwait(true);

        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(slice);
        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes 100-103/5000");
    }

    [Fact]
    public async Task ToResult_AlreadyPartialWithUnknownTotal_UsesTheWildcard()
    {
        var http = await ExecuteAsync(Partial([1, 2], 0, 1, null).ToResult()).ConfigureAwait(true);

        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes 0-1/*");
    }

    [Fact]
    public async Task ToResult_AlreadyPartialInline_OmitsContentDispositionLikeTheWholeFilePath()
    {
        var response = new FileResponse(new MemoryStream([1, 2]), "application/pdf", "doc.pdf")
        {
            Inline = true,
            PartialContent = new FileContentRange(0, 1, 2)
        };

        var http = await ExecuteAsync(response.ToResult()).ConfigureAwait(true);

        http.Response.Headers.Should().NotContainKey("Content-Disposition");
    }

    // ── The RPC hop applies Range itself: ASP.NET only does it for GET/HEAD, and /_pragmatic/invoke
    //    is a POST, so delegating there yields a 200 with the whole file however the range was asked.

    private static readonly byte[] Whole = [.. Enumerable.Range(0, 200).Select(i => (byte)i)];

    private static FileResponse Rangeable(string? etag = "v1")
        => new(new MemoryStream(Whole), "application/pdf", "doc.pdf", EnableRangeProcessing: true)
        {
            ETag = etag
        };

    private static async Task<DefaultHttpContext> ExecuteRpcAsync(
        FileResponse file, string? range = null, string? ifRange = null)
    {
        var http = NewContext();
        http.Request.Method = HttpMethods.Post;
        if (range is not null)
            http.Request.Headers.Range = range;
        if (ifRange is not null)
            http.Request.Headers["If-Range"] = ifRange;

        await file.ToResult(null, includeRpcMetadata: true).ExecuteAsync(http).ConfigureAwait(false);
        return http;
    }

    [Fact]
    public async Task RpcHop_Range_SlicesTheStreamItself()
    {
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=10-19").ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes 10-19/200");
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(Whole[10..20]);
    }

    [Fact]
    public async Task RpcHop_OpenEndedRange_RunsToTheEndOfTheFile()
    {
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=190-").ConfigureAwait(true);

        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes 190-199/200");
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(Whole[190..]);
    }

    [Fact]
    public async Task RpcHop_SuffixRange_TakesTheLastBytes()
    {
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=-15").ConfigureAwait(true);

        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes 185-199/200");
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(Whole[^15..]);
    }

    [Fact]
    public async Task RpcHop_RangeBeyondTheEnd_Answers416WithTheRealLength()
    {
        // Never an empty 206: that tells the client it received the slice it asked for.
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=500-600").ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status416RangeNotSatisfiable);
        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes */200");
        ((MemoryStream)http.Response.Body).ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task RpcHop_RangeEndPastTheEnd_IsClampedRatherThanRefused()
    {
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=195-9999").ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        http.Response.Headers["Content-Range"].ToString().Should().Be("bytes 195-199/200");
    }

    [Fact]
    public async Task RpcHop_MultipleRanges_ServeTheWholeFile()
    {
        // multipart/byteranges is not worth its complexity here, and the whole representation is always
        // a valid answer to a range request.
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=0-9,20-29").ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(Whole);
    }

    [Fact]
    public async Task RpcHop_NoRange_ServesTheWholeFileAndAdvertisesRanges()
    {
        var http = await ExecuteRpcAsync(Rangeable()).ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        http.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(Whole);
    }

    [Fact]
    public async Task RpcHop_RangeWithoutOptIn_IsIgnored()
    {
        // EnableRangeProcessing is the action's decision, exactly as on the local path.
        var file = new FileResponse(new MemoryStream(Whole), "application/pdf", "doc.pdf");

        var http = NewContext();
        http.Request.Headers.Range = "bytes=10-19";
        await file.ToResult(null, includeRpcMetadata: true).ExecuteAsync(http).ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task RpcHop_IfRangeMatchingTheETag_ServesTheRange()
    {
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=0-9", "\"v1\"").ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
    }

    [Fact]
    public async Task RpcHop_IfRangeAgainstAStaleETag_ServesTheWholeFile()
    {
        // The client's cached prefix no longer matches: splicing a range onto it would build a file
        // that never existed.
        var http = await ExecuteRpcAsync(Rangeable(), "bytes=0-9", "\"stale\"").ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(Whole);
    }

    [Fact]
    public async Task RpcHop_IfRangeWithNoValidatorToCompareAgainst_ServesTheWholeFile()
    {
        var http = await ExecuteRpcAsync(Rangeable(etag: null), "bytes=0-9", "\"v1\"").ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task RpcHop_NonSeekableContent_ServesTheWholeFileInsteadOfPretending()
    {
        var file = new FileResponse(
            new NonSeekableStream(Whole), "application/pdf", "doc.pdf", EnableRangeProcessing: true);

        var http = NewContext();
        http.Request.Headers.Range = "bytes=10-19";
        await file.ToResult(null, includeRpcMetadata: true).ExecuteAsync(http).ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
