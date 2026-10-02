namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Represents a file download response.
/// </summary>
/// <remarks>
///     <para>
///         When an endpoint returns a <see cref="FileResponse" />, the source generator
///         maps it to <c>Results.File()</c> with appropriate headers.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Get, "/api/documents/{id}/download")]
/// public partial class DownloadDocument : Endpoint&lt;FileResponse, NotFoundError&gt;
/// {
///     public override async Task&lt;Result&lt;FileResponse, NotFoundError&gt;&gt; HandleAsync(CancellationToken ct)
///     {
///         var doc = await _storage.GetAsync(Id, ct);
///         if (doc is null) return NotFoundError.For("Document", Id);
/// 
///         return new FileResponse(doc.Stream, doc.ContentType, doc.FileName)
///         {
///             ETag = doc.Hash,
///             LastModified = doc.ModifiedAt,
///             EnableRangeProcessing = true
///         };
///     }
/// }
/// </code>
/// </example>
/// <param name="Content">The file content stream.</param>
/// <param name="ContentType">The MIME content type (e.g., "application/pdf").</param>
/// <param name="FileName">Optional file name for Content-Disposition header.</param>
/// <param name="EnableRangeProcessing">Whether to enable range requests (Accept-Ranges: bytes).</param>
public sealed record FileResponse(
    Stream Content,
    string ContentType,
    string? FileName = null,
    bool EnableRangeProcessing = false) : IDisposable
{
    /// <summary>
    ///     Disposes the underlying <see cref="Content" /> stream.
    ///     The caller (endpoint handler or <c>FileResponseExtensions.ToResult</c>) is responsible
    ///     for disposing the response once the stream has been written to the HTTP response.
    /// </summary>
    public void Dispose() => Content.Dispose();
    /// <summary>
    ///     Gets or sets the ETag for caching and conditional requests.
    /// </summary>
    /// <remarks>
    ///     When set, the response includes an <c>ETag</c> header.
    ///     The endpoint will automatically handle <c>If-None-Match</c> requests.
    /// </remarks>
    public string? ETag { get; init; }

    /// <summary>
    ///     Gets or sets the last modified date for caching.
    /// </summary>
    /// <remarks>
    ///     When set, the response includes a <c>Last-Modified</c> header.
    ///     The endpoint will automatically handle <c>If-Modified-Since</c> requests.
    /// </remarks>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    ///     Gets or sets whether to display inline or as attachment.
    /// </summary>
    /// <remarks>
    ///     When <c>true</c>, uses <c>Content-Disposition: inline</c> (display in browser).
    ///     When <c>false</c> (default), uses <c>Content-Disposition: attachment</c> (download).
    /// </remarks>
    public bool Inline { get; init; }

    /// <summary>
    ///     Set when <see cref="Content" /> is <b>already</b> a partial response — the bytes of one range,
    ///     not the whole file. <c>null</c> (default) means <see cref="Content" /> is the complete
    ///     representation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This exists because of one specific way a download breaks silently. A remote boundary that
    ///         honours <c>Range</c> answers <c>206</c> with just the requested bytes; if the calling host
    ///         then hands that stream to <c>Results.File(..., enableRangeProcessing: true)</c> on its own
    ///         request — which carries the same <c>Range</c> header — the range is applied a second time,
    ///         to a stream that is already an excerpt. The caller gets a range of a range, under a
    ///         plausible status and a plausible <c>Content-Range</c>: no exception, no diagnostic, just
    ///         wrong bytes. Even without the second application the naive path is wrong, because it
    ///         reports partial bytes as <c>200 OK</c> with a <c>Content-Length</c> the client reads as the
    ///         whole file.
    ///     </para>
    ///     <para>
    ///         When this is set, <c>FileResponseExtensions.ToResult</c> stops interpreting and starts
    ///         forwarding: status <c>206</c>, <c>Content-Range</c> and <c>Accept-Ranges</c> as received,
    ///         body copied through.
    ///     </para>
    ///     <para>
    ///         It lives on <see cref="FileResponse" /> rather than in a separate remote-only type because
    ///         a boundary interface returns <c>Result&lt;FileResponse, IError&gt;</c> whether the boundary
    ///         is local or remote — that identity is the whole point of <c>[RemoteBoundary]</c>. A distinct
    ///         return type for the remote path would make the topology visible in the contract and force
    ///         every caller to branch on it.
    ///     </para>
    /// </remarks>
    public FileContentRange? PartialContent { get; init; }
}