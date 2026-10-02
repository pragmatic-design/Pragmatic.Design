namespace Pragmatic.Documents.Pdf;

/// <summary>
/// Options for PDF rendering: batching, fonts, image policy.
/// </summary>
public sealed record PdfRenderOptions
{
    /// <summary>
    /// Number of pages per batch for large documents. 0 = no batching (compile entire document at once).
    /// When set (e.g. 20), documents with more pages are compiled in batches and merged.
    /// This bounds memory usage for large documents at the cost of a merge step.
    /// </summary>
    public int BatchPageCount { get; init; }

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

    /// <summary>Default options (no batching, no image limits).</summary>
    public static PdfRenderOptions Default { get; } = new();

    /// <summary>Server preset: batch 20 pages to bound memory on large documents.</summary>
    /// <remarks>
    /// Deliberately does NOT set <see cref="MaxImageDimension"/>: image downscaling is not yet
    /// wired into the native engine and a non-zero value makes the renderer throw
    /// <see cref="NotSupportedException"/> — which would make this preset unusable.
    /// </remarks>
    public static PdfRenderOptions Server { get; } = new()
    {
        BatchPageCount = 20
    };
}
