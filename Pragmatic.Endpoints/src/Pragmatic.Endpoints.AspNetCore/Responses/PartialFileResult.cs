using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Writes a <see cref="FileResponse" /> whose content is <b>already</b> one byte range
///     (<see cref="FileResponse.PartialContent" />) straight through: status <c>206</c>, the range it was
///     given, the bytes it was given.
/// </summary>
/// <remarks>
///     <para>
///         The point of this result is what it does <i>not</i> do. Handing an already-partial stream to
///         <c>Results.File(..., enableRangeProcessing: true)</c> on a request that carries the same
///         <c>Range</c> header applies the range twice — the caller receives a range of a range, with a
///         status and a <c>Content-Range</c> that both look right. Nothing throws. This result never
///         re-interprets the content, so that second application cannot happen.
///     </para>
///     <para>
///         It also refuses to let partial bytes pass as a whole file: forwarding a <c>206</c> body under
///         <c>200 OK</c> tells the client the file <i>is</i> that short, which is the same corruption
///         wearing a different disguise.
///     </para>
/// </remarks>
internal sealed class PartialFileResult : IResult
{
    private readonly FileResponse _file;
    private readonly bool _includeRpcMetadata;
    private readonly FileContentRange _range;

    public PartialFileResult(FileResponse file, FileContentRange range, bool includeRpcMetadata)
    {
        _file = file;
        _range = range;
        _includeRpcMetadata = includeRpcMetadata;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(httpContext);

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status206PartialContent;
        response.ContentType = _file.ContentType;
        response.ContentLength = _range.Length;
        response.Headers[HeaderNames.AcceptRanges] = "bytes";
        response.Headers[HeaderNames.ContentRange] = _range.ToHeaderValue();

        // Same rule as the whole-file path: an inline file carries no Content-Disposition at all.
        if (!_file.Inline && !string.IsNullOrEmpty(_file.FileName))
        {
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(_file.FileName!);
            response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        }

        if (!string.IsNullOrEmpty(_file.ETag))
            response.Headers[HeaderNames.ETag] = $"\"{_file.ETag}\"";

        if (_file.LastModified.HasValue)
            response.GetTypedHeaders().LastModified = _file.LastModified;

        if (_includeRpcMetadata)
            FileMetadataHeaderWriter.Write(response, _file);

        try
        {
            await CopyRangeAsync(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            // Results.File disposes the stream it was handed; so does this.
            await _file.Content.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Copies exactly <see cref="FileContentRange.Length" /> bytes from the current position.
    /// </summary>
    /// <remarks>
    ///     Bounded, not "copy the rest", because this result serves two shapes: a stream that already
    ///     holds only the range (forwarded from a remote boundary) and a whole-file stream seeked to the
    ///     start of the range. In the second the remainder of the file sits right behind the range, and
    ///     an unbounded copy would send it after a <c>Content-Length</c> that says otherwise.
    /// </remarks>
    private async Task CopyRangeAsync(Stream destination, CancellationToken ct)
    {
        var remaining = _range.Length;
        var buffer = new byte[(int)Math.Min(remaining, 64 * 1024)];

        while (remaining > 0)
        {
            var wanted = (int)Math.Min(remaining, buffer.Length);
            var read = await _file.Content.ReadAsync(buffer.AsMemory(0, wanted), ct).ConfigureAwait(false);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
