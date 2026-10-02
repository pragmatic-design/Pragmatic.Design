namespace Pragmatic.Imaging;

/// <summary>
/// Categorises why an <see cref="ImagingException"/> was raised, so callers can branch
/// on the failure kind without parsing the message.
/// </summary>
/// <remarks>
/// Argument-shape mistakes (a zero <c>Resize</c> dimension, an out-of-bounds <c>Crop</c>,
/// a non-quarter-turn <c>Rotate</c>) surface as <see cref="System.ArgumentException"/>,
/// not <see cref="ImagingException"/>. This enum covers the imaging-pipeline failures:
/// decode/encode, safety-limit violations, and native errors.
/// </remarks>
public enum ImagingError
{
    /// <summary>An unclassified failure from the native library.</summary>
    NativeError = 0,

    /// <summary>The input could not be decoded (corrupt, truncated, or unsupported).</summary>
    DecodeFailed,

    /// <summary>Encoding the current image to the requested format failed.</summary>
    EncodeFailed,

    /// <summary>The requested output format is not a valid encoder target.</summary>
    UnsupportedFormat,

    /// <summary>The encoded input exceeded <see cref="ImagingOptions.MaxInputBytes"/>.</summary>
    InputTooLarge,

    /// <summary>The decoded width exceeded <see cref="ImagingOptions.MaxWidth"/>.</summary>
    MaxWidthExceeded,

    /// <summary>The decoded height exceeded <see cref="ImagingOptions.MaxHeight"/>.</summary>
    MaxHeightExceeded,

    /// <summary>The decoded megapixel count exceeded <see cref="ImagingOptions.MaxMegapixels"/>.</summary>
    MaxMegapixelsExceeded,

    /// <summary>The decoded format was not in <see cref="ImagingOptions.AllowedFormats"/>.</summary>
    FormatNotAllowed,
}
