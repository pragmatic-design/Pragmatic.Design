using System.Net;
using System.Net.Http.Headers;

namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Rebuilds a <see cref="FileResponse" /> from an <see cref="HttpResponseMessage" /> — the client
///     half of a file-returning endpoint or remote boundary action.
/// </summary>
/// <remarks>
///     The caller must have issued the request with
///     <see cref="HttpCompletionOption.ResponseHeadersRead" />; otherwise <c>HttpClient</c> has already
///     buffered the whole body in memory and the streaming this type preserves buys nothing.
/// </remarks>
public static class RemoteFileResponse
{
    /// <summary>Content type used when the response carries none.</summary>
    public const string DefaultContentType = "application/octet-stream";

    /// <summary>File name used when the response carries no usable <c>Content-Disposition</c>.</summary>
    public const string DefaultFileName = "download";

    /// <summary>
    ///     Wraps <paramref name="response" /> in a <see cref="FileResponse" />, transferring ownership of
    ///     the response to the returned <see cref="FileResponse.Content" /> stream.
    /// </summary>
    /// <param name="response">
    ///     A successful response (<c>200</c> or <c>206</c>), still undisposed. On return the caller must
    ///     NOT dispose it: the returned stream does that, and doing it earlier truncates the download.
    /// </param>
    /// <param name="fallbackFileName">File name to use when the response does not name one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="FileResponse" /> streaming the response body.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The response is <c>206 Partial Content</c> but carries no usable <c>Content-Range</c>. The bytes
    ///     are then an excerpt of unknown position: forwarding them as a whole file would corrupt the
    ///     download silently, so this fails loudly instead.
    /// </exception>
    public static async Task<FileResponse> ReadAsync(
        HttpResponseMessage response,
        string? fallbackFileName = null,
        CancellationToken ct = default)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(response);

        // Read before taking ownership of the stream: a protocol violation must not leave a half-built
        // FileResponse holding the connection.
        var partial = ReadPartialContent(response);

        Stream content;
        try
        {
            var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            content = new HttpResponseOwningStream(body, response);
        }
        catch
        {
            // Ownership never reached the caller, so it is still ours to release.
            response.Dispose();
            throw;
        }

        return new FileResponse(
            content,
            response.Content.Headers.ContentType?.MediaType ?? DefaultContentType,
            ReadOutOfBandFileName(response)
            ?? ReadFileName(response.Content.Headers.ContentDisposition)
            ?? fallbackFileName
            ?? DefaultFileName)
        {
            ETag = ReadETag(response.Headers.ETag),
            LastModified = response.Content.Headers.LastModified,
            Inline = PragmaticFileHeaders.DecodeInline(ReadHeader(response, PragmaticFileHeaders.Inline)),
            PartialContent = partial

            // EnableRangeProcessing is deliberately left false. The remote end already applied the range
            // and owns that semantics; re-enabling it here would let the calling host apply the caller's
            // Range a second time, to content that is already an excerpt.
        };
    }

    /// <summary>
    ///     Reads the range a <c>206</c> response covers. A <c>200</c> is the whole representation and has
    ///     no range, even if it happens to carry a stray <c>Content-Range</c>.
    /// </summary>
    private static FileContentRange? ReadPartialContent(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.PartialContent)
            return null;

        var range = response.Content.Headers.ContentRange;
        if (range is { HasRange: true, From: { } from, To: { } to })
            return new FileContentRange(from, to, range.Length);

        response.Dispose();
        throw new InvalidOperationException(
            "The remote boundary answered 206 Partial Content without a usable Content-Range header. " +
            "The body is an excerpt of unknown position and cannot be forwarded safely.");
    }

    /// <summary>
    ///     Reads the file name from the out-of-band header, which — unlike <c>Content-Disposition</c> —
    ///     is present even for an inline file.
    /// </summary>
    private static string? ReadOutOfBandFileName(HttpResponseMessage response)
        => PragmaticFileHeaders.DecodeFileName(ReadHeader(response, PragmaticFileHeaders.FileName));

    private static string? ReadHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>
    ///     Prefers <c>filename*</c> (RFC 5987, carries the charset) over the plain <c>filename</c>,
    ///     which cannot express non-ASCII names.
    /// </summary>
    private static string? ReadFileName(ContentDispositionHeaderValue? disposition)
    {
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return name!.Trim('"');
    }

    /// <summary>
    ///     <see cref="FileResponse.ETag" /> holds the bare validator: the transport quotes are re-added
    ///     when the value is written back out, so keeping them here would double them.
    /// </summary>
    private static string? ReadETag(EntityTagHeaderValue? etag)
    {
        var tag = etag?.Tag;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        return tag!.Trim('"');
    }
}
