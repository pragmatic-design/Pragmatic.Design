using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging.Benchmarks.Pipelines;

/// <summary>
///     The same stages on the Rust library, called directly: the baseline. Pragmatic.Imaging no longer
///     reaches it for PNG and JPEG, so going through the module would time the managed engine twice.
/// </summary>
public static unsafe class NativePipeline
{
    public static int Decode(byte[] encoded)
    {
        using var pixels = RustImaging.Decode(encoded, out var width, out _);
        return (int)width;
    }

    public static int DecodeThumbnail(byte[] encoded)
    {
        using var thumbnail = Thumbnail(encoded, out var width, out _);
        return (int)width;
    }

    public static byte[] DecodeThumbnailEncodeJpeg(byte[] encoded)
    {
        using var thumbnail = Thumbnail(encoded, out var width, out var height);
        return RustImaging.Encode((byte*)thumbnail.Ptr, thumbnail.Length, width, height, ImageFormat.Jpeg, Fixtures.JpegQuality);
    }

    public static byte[] DecodeEncodePng(byte[] encoded)
    {
        using var pixels = RustImaging.Decode(encoded, out var width, out var height);
        return RustImaging.Encode((byte*)pixels.Ptr, pixels.Length, width, height, ImageFormat.Png, 90);
    }

    /// <summary>The thumbnail from pixels already in memory: the resize alone.</summary>
    public static int ThumbnailFromRgba(byte* rgba, nuint length, uint width, uint height)
    {
        var (w, h) = RustImaging.ThumbnailSize(width, height, Fixtures.ThumbnailWidth, Fixtures.ThumbnailHeight);
        using var thumbnail = RustImaging.Resize(rgba, length, width, height, w, h);
        return (int)w;
    }

    /// <summary>The encoder alone, from pixels already in memory.</summary>
    public static byte[] EncodeJpeg(byte* rgba, nuint length, uint width, uint height)
        => RustImaging.Encode(rgba, length, width, height, ImageFormat.Jpeg, Fixtures.JpegQuality);

    private static NativeBuffer Thumbnail(byte[] encoded, out uint width, out uint height)
    {
        using var pixels = RustImaging.Decode(encoded, out var sourceWidth, out var sourceHeight);
        (width, height) = RustImaging.ThumbnailSize(sourceWidth, sourceHeight, Fixtures.ThumbnailWidth, Fixtures.ThumbnailHeight);
        return RustImaging.Resize((byte*)pixels.Ptr, pixels.Length, sourceWidth, sourceHeight, width, height);
    }
}
