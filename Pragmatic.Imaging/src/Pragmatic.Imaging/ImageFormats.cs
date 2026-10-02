namespace Pragmatic.Imaging;

/// <summary>
///     Which file extensions this library can decode, and what it calls them.
/// </summary>
/// <remarks>
///     <para>
///         The set belongs here rather than in each caller. A consumer deciding whether to hand a file
///         to the decoder — a generated attachment upload, a validator, an importer — otherwise keeps
///         its own copy of the list, and a copy of a list ages in silence: the day this library learns
///         a format, every copy is wrong and nothing says so.
///     </para>
///     <para>
///         ⚠️ <b>An extension is a claim, not a fact.</b> This answers "did the caller say this is an
///         image", which is the question worth asking <em>before</em> decoding — a <c>.pdf</c> has no
///         business reaching the decoder at all. Whether the bytes really are one is answered by
///         <see cref="ImageInfo.FromBytes" />, which throws
///         <see cref="ImagingException" /> when they are not, so a file that names an image extension
///         and does not decode is a caller error rather than a file to leave alone.
///     </para>
/// </remarks>
public static class ImageFormats
{
    /// <summary>
    ///     The format this library associates with a file extension, or
    ///     <see cref="ImageFormat.Unknown" />.
    /// </summary>
    /// <param name="extension">
    ///     With or without the leading dot, in any case: <c>".PNG"</c>, <c>"png"</c> and <c>".png"</c>
    ///     all answer <see cref="ImageFormat.Png" />.
    /// </param>
    public static ImageFormat FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return ImageFormat.Unknown;

        var ext = extension!.TrimStart('.');

        // Ordinal-ignore-case rather than the current culture: a Turkish locale lower-cases 'I' to a
        // dotless 'ı', so a culture-sensitive comparison would answer Unknown for ".TIFF" on one
        // machine and Tiff on another.
        return ext switch
        {
            _ when Is(ext, "png") => ImageFormat.Png,
            _ when Is(ext, "jpg") || Is(ext, "jpeg") => ImageFormat.Jpeg,
            _ when Is(ext, "webp") => ImageFormat.WebP,
            _ when Is(ext, "avif") => ImageFormat.Avif,
            _ when Is(ext, "gif") => ImageFormat.Gif,
            _ when Is(ext, "bmp") => ImageFormat.Bmp,
            _ when Is(ext, "tif") || Is(ext, "tiff") => ImageFormat.Tiff,
            _ => ImageFormat.Unknown
        };
    }

    /// <summary>Whether a file with this extension claims to be an image this library decodes.</summary>
    public static bool IsKnownExtension(string? extension)
        => FromExtension(extension) != ImageFormat.Unknown;

    /// <summary>The canonical extension of a format, leading dot included.</summary>
    /// <remarks>
    ///     For <see cref="ImageFormat.Unknown" /> there is nothing to answer, so it throws rather than
    ///     inventing one: a caller naming a file after a format it does not know has a bug, and a
    ///     <c>".bin"</c> would hide it.
    /// </remarks>
    public static string ToExtension(ImageFormat format) => format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.WebP => ".webp",
        ImageFormat.Avif => ".avif",
        ImageFormat.Gif => ".gif",
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Tiff => ".tiff",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format,
            "Unknown has no extension: it is what a header that could not be read reports.")
    };

    private static bool Is(string value, string name)
        => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
}
