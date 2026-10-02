using Microsoft.AspNetCore.Http;

namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Writes the out-of-band <see cref="PragmaticFileHeaders" /> onto a response. Used by every result
///     that serves a <see cref="FileResponse" /> across an RPC hop, so the two shapes (whole file and
///     already-partial file) cannot drift apart.
/// </summary>
internal static class FileMetadataHeaderWriter
{
    /// <summary>
    ///     Emits the metadata that <c>Content-Disposition</c> cannot carry.
    /// </summary>
    /// <param name="response">The HTTP response to write to.</param>
    /// <param name="file">The file being served.</param>
    public static void Write(HttpResponse response, FileResponse file)
    {
        if (!string.IsNullOrEmpty(file.FileName))
            response.Headers[PragmaticFileHeaders.FileName] = PragmaticFileHeaders.EncodeFileName(file.FileName!);

        response.Headers[PragmaticFileHeaders.Inline] = PragmaticFileHeaders.EncodeInline(file.Inline);
    }
}
