using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Pragmatic.Endpoints.Responses;

namespace Pragmatic.Endpoints.Extensions;

/// <summary>
///     Extension methods for converting FileResponse to HTTP results.
/// </summary>
public static class FileResponseExtensions
{
    /// <summary>
    ///     Converts a FileResponse to an IResult for Minimal APIs.
    /// </summary>
    /// <param name="response">The file response.</param>
    /// <param name="httpContext">The HTTP context for conditional request handling.</param>
    /// <param name="includeRpcMetadata">
    ///     Whether to also emit the out-of-band <see cref="PragmaticFileHeaders" />. Set only on the
    ///     Pragmatic RPC hop (<c>POST /_pragmatic/invoke</c>), where the caller is another Pragmatic host
    ///     rebuilding the <see cref="FileResponse" /> and needs the metadata
    ///     <c>Content-Disposition</c> cannot carry. A public endpoint leaves it <c>false</c> and its
    ///     response is byte-for-byte what it always was.
    /// </param>
    /// <returns>An IResult representing the file response.</returns>
    public static IResult ToResult(
        this FileResponse response,
        HttpContext? httpContext = null,
        bool includeRpcMetadata = false)
    {
        var responseETag = !string.IsNullOrEmpty(response.ETag)
            ? new EntityTagHeaderValue($"\"{response.ETag}\"")
            : null;

        // Handle If-None-Match (ETag conditional) — RFC 7232 compliant
        if (httpContext is not null && responseETag is not null)
        {
            var ifNoneMatch = httpContext.Request.GetTypedHeaders().IfNoneMatch;
            if (ifNoneMatch is not null && ifNoneMatch.Any(etag =>
                    etag.Tag == EntityTagHeaderValue.Any.Tag || etag.Compare(responseETag, useStrongComparison: true)))
            {
                response.Content?.Dispose();
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
        }

        // Handle If-Modified-Since
        if (httpContext is not null && response.LastModified.HasValue)
        {
            var ifModifiedSince = httpContext.Request.GetTypedHeaders().IfModifiedSince;
            if (ifModifiedSince.HasValue && response.LastModified.Value <= ifModifiedSince.Value)
            {
                response.Content?.Dispose();
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
        }

        // Already one range: forward it, never re-range it. Applying the caller's Range to content that
        // is already an excerpt yields a range of a range — plausible status, plausible Content-Range,
        // wrong bytes, no error anywhere.
        if (response.PartialContent is { } partial)
            return new PartialFileResult(response, partial, includeRpcMetadata);

        // The RPC hop is a POST, and ASP.NET applies Range only to GET/HEAD — so on that hop the range
        // is ours to honour, along with the out-of-band metadata.
        if (includeRpcMetadata)
            return new RpcFileResult(response);

        return Results.File(
            response.Content,
            response.ContentType,
            response.Inline ? null : response.FileName,
            response.LastModified,
            responseETag,
            response.EnableRangeProcessing);
    }
}