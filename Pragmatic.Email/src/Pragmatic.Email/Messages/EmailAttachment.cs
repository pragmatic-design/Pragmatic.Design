namespace Pragmatic.Email;

/// <summary>
///     Email attachment or inline image.
/// </summary>
/// <remarks>
///     <para>
///         <b>Inline coupling.</b> <see cref="ContentId"/> is only meaningful when
///         <see cref="IsInline"/> is <c>true</c>; setting it without
///         <c>IsInline = true</c> has no effect (the HTML body cannot reference
///         a non-inline attachment via the <c>cid:</c> scheme). Conversely, an
///         inline attachment without a <see cref="ContentId"/> can be added to
///         the MIME structure but cannot be addressed from HTML.
///     </para>
/// </remarks>
public sealed record EmailAttachment
{
    /// <summary>File name shown to the recipient.</summary>
    public required string FileName { get; init; }

    /// <summary>MIME content type (e.g., "application/pdf", "image/png").</summary>
    public required string ContentType { get; init; }

    /// <summary>Attachment binary data.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>
    ///     True for inline images referenced via Content-ID in HTML.
    ///     Requires <see cref="ContentId"/> to be set for the HTML body to
    ///     reference the image via <c>&lt;img src="cid:{ContentId}"&gt;</c>.
    /// </summary>
    public bool IsInline { get; init; }

    /// <summary>
    ///     Content-ID for inline images (referenced as <c>cid:{ContentId}</c> in HTML).
    ///     Has no effect unless <see cref="IsInline"/> is <c>true</c>.
    /// </summary>
    public string? ContentId { get; init; }
}
