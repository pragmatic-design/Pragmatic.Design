namespace Pragmatic.Documents.Pdf;

/// <summary>
/// Options for PDF rendering: fonts, image policy.
/// </summary>
/// <remarks>
/// There is no memory option: the native engine lays a long document out in chunks on its own, so its
/// peak follows the chunk and not the document (see <c>pdf-rendering.md</c>, <i>Memory</i>).
/// </remarks>
public sealed record PdfRenderOptions
{
    /// <summary>
    /// Custom font files (TTF/OTF bytes) to register in the Typst engine.
    /// These fonts are available in addition to system fonts.
    /// </summary>
    /// <remarks>
    /// Not yet wired into the native engine: setting this throws <see cref="NotSupportedException"/>
    /// at render time rather than being silently ignored.
    /// </remarks>
    public IReadOnlyList<byte[]>? CustomFonts { get; init; }

    /// <summary>
    /// Maximum image dimension (width or height) in pixels before auto-downscale.
    /// 0 = no limit. Images larger than this are resized before embedding.
    /// Reduces PDF size significantly for documents with large photos.
    /// </summary>
    /// <remarks>
    /// Not yet wired into the native engine: setting a non-zero value throws
    /// <see cref="NotSupportedException"/> at render time rather than being silently ignored.
    /// </remarks>
    public int MaxImageDimension { get; init; }

    /// <summary>
    /// JPEG quality for image pre-compression (1-100). Only applied when MaxImageDimension triggers a resize.
    /// Default: 85. Has no effect unless <see cref="MaxImageDimension"/> is set (which is itself not yet supported).
    /// </summary>
    public byte ImageQuality { get; init; } = 85;

    /// <summary>Default options (no custom fonts, no image limits).</summary>
    public static PdfRenderOptions Default { get; } = new();
}
