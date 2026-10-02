namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     The out-of-band metadata a <see cref="FileResponse" /> carries across a Pragmatic RPC hop
///     (<c>POST /_pragmatic/invoke</c>) — and the single place their names and encoding are defined.
///     Both halves of the hop use this type; there is no second, twin constant to drift from.
/// </summary>
/// <remarks>
///     <para>
///         <c>Content-Disposition</c> cannot carry the whole record. With <see cref="FileResponse.Inline" />
///         set, HTTP semantics say the header is simply absent — and the file name went with it, because
///         that header was the only place it lived. The flag itself has no standard header at all. Both
///         therefore need a channel of their own.
///     </para>
///     <para>
///         These headers are <b>additional</b>, never a substitute: <c>Content-Disposition: attachment</c>
///         is still emitted verbatim whenever the file is an attachment, so a plain HTTP client that knows
///         nothing about Pragmatic sees exactly what it saw before.
///     </para>
///     <para>
///         <see cref="FileResponse.ETag" /> and <see cref="FileResponse.LastModified" /> need no header of
///         their own — they already travel as the standard <c>ETag</c> and <c>Last-Modified</c> headers and
///         are read back by <see cref="RemoteFileResponse" />. <c>Last-Modified</c> is an HTTP-date, so it
///         survives to whole-second precision and no finer.
///     </para>
/// </remarks>
public static class PragmaticFileHeaders
{
    /// <summary>
    ///     The file name, percent-encoded UTF-8 (see <see cref="EncodeFileName" />).
    /// </summary>
    public const string FileName = "X-Pragmatic-File-Name";

    /// <summary>
    ///     <c>true</c> when the file is meant to be displayed rather than downloaded
    ///     (<see cref="FileResponse.Inline" />). Absent means <c>false</c>.
    /// </summary>
    public const string Inline = "X-Pragmatic-File-Inline";

    /// <summary>
    ///     Encodes a file name for transport in a header value.
    /// </summary>
    /// <remarks>
    ///     Percent-encoded UTF-8: header values are not a Unicode channel, and an un-encoded name would
    ///     either be mangled by the Latin-1 round-trip or — with a CR/LF in it — split the response.
    ///     ASCII names stay readable, which keeps the wire debuggable.
    /// </remarks>
    /// <param name="fileName">The raw file name.</param>
    /// <returns>The encoded header value.</returns>
    public static string EncodeFileName(string fileName)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(fileName);

        return Uri.EscapeDataString(fileName);
    }

    /// <summary>
    ///     Reverses <see cref="EncodeFileName" />.
    /// </summary>
    /// <param name="headerValue">The header value, or <c>null</c> when the header is absent.</param>
    /// <returns>The decoded file name, or <c>null</c> when there is nothing usable to decode.</returns>
    public static string? DecodeFileName(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return null;

        try
        {
            var decoded = Uri.UnescapeDataString(headerValue!);
            return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
        }
        catch (UriFormatException)
        {
            // A malformed value is a broken peer, not a reason to fail the download: fall back to
            // whatever Content-Disposition (or the caller's default) offers.
            return null;
        }
    }

    /// <summary>
    ///     Reads the <see cref="Inline" /> flag. Anything other than a literal <c>true</c> is <c>false</c>,
    ///     including the header being absent — the conservative reading, since guessing "display in the
    ///     browser" wrongly is the answer with consequences.
    /// </summary>
    /// <param name="headerValue">The header value, or <c>null</c> when the header is absent.</param>
    /// <returns>Whether the file is inline.</returns>
    public static bool DecodeInline(string? headerValue)
        => string.Equals(headerValue, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Encodes the <see cref="Inline" /> flag.</summary>
    /// <param name="inline">Whether the file is inline.</param>
    /// <returns>The header value.</returns>
    public static string EncodeInline(bool inline) => inline ? "true" : "false";
}
