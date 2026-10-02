using System.Globalization;

namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     The byte range a <see cref="FileResponse" /> already contains — set only when the content is
///     <b>already</b> a partial response and must be passed through untouched.
/// </summary>
/// <remarks>
///     <para>
///         Both ends are inclusive, as in the HTTP <c>Content-Range</c> header this maps to.
///     </para>
/// </remarks>
/// <param name="From">First byte offset, inclusive.</param>
/// <param name="To">Last byte offset, inclusive.</param>
/// <param name="TotalLength">Total length of the complete representation, or <c>null</c> when unknown.</param>
public sealed record FileContentRange(long From, long To, long? TotalLength)
{
    /// <summary>Number of bytes this range covers.</summary>
    public long Length => To - From + 1;

    /// <summary>
    ///     Renders the range as an HTTP <c>Content-Range</c> value (<c>bytes from-to/total</c>).
    /// </summary>
    /// <returns>The header value.</returns>
    public string ToHeaderValue()
    {
        var total = TotalLength?.ToString(CultureInfo.InvariantCulture) ?? "*";
        return string.Format(
            CultureInfo.InvariantCulture, "bytes {0}-{1}/{2}", From, To, total);
    }
}
