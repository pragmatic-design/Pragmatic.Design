using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Serves a <see cref="FileResponse" /> on the Pragmatic RPC hop (<c>POST /_pragmatic/invoke</c>):
///     the out-of-band <see cref="PragmaticFileHeaders" />, plus range handling this hop has to do for
///     itself.
/// </summary>
/// <remarks>
///     <para>
///         ASP.NET's own file results apply <c>Range</c> only to <c>GET</c> and <c>HEAD</c>
///         (<c>FileResultHelper</c> guards on the method). The invoke endpoint is a <c>POST</c> — the
///         payload is the action — so handing the work to <c>Results.File(..., enableRangeProcessing:
///         true)</c> silently produces a <c>200</c> with the whole file, no matter what <c>Range</c> the
///         calling host propagated. Honouring the range here is what keeps a seek into a 500 MB object
///         from moving 500 MB across the network.
///     </para>
///     <para>
///         A single range only, matching what ASP.NET supports: multipart/byteranges is not worth its
///         complexity for boundary-to-boundary RPC, and a client asking for several ranges gets the whole
///         representation, which is always a valid answer.
///     </para>
/// </remarks>
internal sealed class RpcFileResult : IResult
{
    private readonly FileResponse _file;

    public RpcFileResult(FileResponse file) => _file = file;

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(httpContext);

        FileMetadataHeaderWriter.Write(httpContext.Response, _file);

        // Range needs a length and a seek. A non-seekable body is streamed whole — the honest answer,
        // since slicing it would mean reading and discarding exactly what we are trying not to move.
        if (!_file.EnableRangeProcessing || !_file.Content.CanSeek)
            return WriteWholeAsync(httpContext);

        var requested = SingleRequestedRange(httpContext);
        if (requested is null || !IfRangeMatches(httpContext))
            return WriteWholeAsync(httpContext);

        var length = _file.Content.Length;
        var resolved = Resolve(requested, length);

        if (resolved is null)
            return WriteUnsatisfiableAsync(httpContext, length);

        _file.Content.Seek(resolved.From, SeekOrigin.Begin);
        return new PartialFileResult(_file, resolved, includeRpcMetadata: false).ExecuteAsync(httpContext);
    }

    private Task WriteWholeAsync(HttpContext httpContext)
    {
        if (_file.EnableRangeProcessing)
            httpContext.Response.Headers[HeaderNames.AcceptRanges] = "bytes";

        var responseETag = !string.IsNullOrEmpty(_file.ETag)
            ? new EntityTagHeaderValue($"\"{_file.ETag}\"")
            : null;

        // enableRangeProcessing: false — this hop already decided, above, that the whole file is the
        // answer. Letting ASP.NET reconsider would put two range implementations on one response.
        return Results.File(
                _file.Content,
                _file.ContentType,
                _file.Inline ? null : _file.FileName,
                _file.LastModified,
                responseETag,
                enableRangeProcessing: false)
            .ExecuteAsync(httpContext);
    }

    /// <summary>
    ///     RFC 9110 §15.5.17: an unsatisfiable range is <c>416</c> with <c>Content-Range: bytes */len</c>
    ///     and no body — never an empty <c>206</c>, which would tell the client it received the slice it
    ///     asked for.
    /// </summary>
    private async Task WriteUnsatisfiableAsync(HttpContext httpContext, long length)
    {
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
        response.Headers[HeaderNames.AcceptRanges] = "bytes";
        response.Headers[HeaderNames.ContentRange] = $"bytes */{length}";
        response.ContentLength = 0;

        await _file.Content.DisposeAsync().ConfigureAwait(false);
    }

    private static RangeItemHeaderValue? SingleRequestedRange(HttpContext httpContext)
    {
        var range = httpContext.Request.GetTypedHeaders().Range;

        return range is not null
               && range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)
               && range.Ranges.Count == 1
            ? range.Ranges.Single()
            : null;
    }

    /// <summary>
    ///     Evaluates <c>If-Range</c>: when the client's validator no longer matches, its cached prefix is
    ///     stale and the range would splice two different representations together, so the whole file is
    ///     served instead.
    /// </summary>
    private bool IfRangeMatches(HttpContext httpContext)
    {
        var ifRange = httpContext.Request.GetTypedHeaders().IfRange;
        if (ifRange is null)
            return true;

        if (ifRange.EntityTag is { } tag)
        {
            return !string.IsNullOrEmpty(_file.ETag)
                   && tag.Compare(new EntityTagHeaderValue($"\"{_file.ETag}\""), useStrongComparison: true);
        }

        if (ifRange.LastModified is { } modified)
        {
            // HTTP-dates carry whole seconds; comparing any finer would reject every valid If-Range.
            return _file.LastModified.HasValue
                   && Truncate(_file.LastModified.Value) == Truncate(modified);
        }

        return false;
    }

    private static DateTimeOffset Truncate(DateTimeOffset value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Offset);

    /// <summary>
    ///     Normalises a requested range against the real length. Returns <c>null</c> when nothing of the
    ///     range lies inside the representation.
    /// </summary>
    private static FileContentRange? Resolve(RangeItemHeaderValue requested, long length)
    {
        if (length == 0)
            return null;

        long from;
        long to;

        if (requested.From is { } start)
        {
            if (start >= length)
                return null;

            from = start;
            to = requested.To is { } end ? Math.Min(end, length - 1) : length - 1;
        }
        else if (requested.To is { } suffixLength)
        {
            if (suffixLength <= 0)
                return null;

            from = Math.Max(0, length - suffixLength);
            to = length - 1;
        }
        else
        {
            return null;
        }

        return to < from ? null : new FileContentRange(from, to, length);
    }
}
