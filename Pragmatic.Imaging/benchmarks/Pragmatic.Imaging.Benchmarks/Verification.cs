using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;
using SkiaSharp;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     Checks, before anything is timed, that both engines compute what the benchmark names say: the
///     same geometry from the same input, and output an independent decoder reads back.
/// </summary>
public static class Verification
{
    public static void Run()
    {
        Check(NativePipeline.Decode(Fixtures.Jpeg4Mp) == Fixtures.JpegWidth, "native decode width");
        Check(ManagedPipeline.Decode(Fixtures.Jpeg4Mp) == Fixtures.JpegWidth, "managed decode width");

        var nativeThumb = NativePipeline.DecodeThumbnail(Fixtures.Jpeg4Mp);
        var managedThumb = ManagedPipeline.DecodeThumbnail(Fixtures.Jpeg4Mp);
        Check(nativeThumb == managedThumb, $"thumbnail widths differ: native {nativeThumb}, managed {managedThumb}");

        CheckDecodes(NativePipeline.DecodeThumbnailEncodeJpeg(Fixtures.Jpeg4Mp), managedThumb, "native pipeline");
        CheckDecodes(ManagedPipeline.DecodeThumbnailEncodeJpeg(Fixtures.Jpeg4Mp), managedThumb, "managed pipeline");
        var nativePng = NativePipeline.DecodeEncodePng(Fixtures.Png2Mp);
        CheckDecodes(nativePng, Fixtures.PngWidth, "native PNG round trip");
        CheckDecodes(ManagedPipeline.DecodeEncodePng(Fixtures.Png2Mp), Fixtures.PngWidth, "managed PNG round trip");

        using var rgba = new PixelBuffer(Fixtures.JpegWidth, Fixtures.JpegHeight);
        Fixtures.Rgba4Mp.CopyTo(rgba.Pixels);
        using var small = new PixelBuffer(Fixtures.SmallWidth, Fixtures.SmallHeight);
        Fixtures.RgbaSmall.CopyTo(small.Pixels);
        unsafe
        {
            var nativeResize = NativePipeline.ThumbnailFromRgba(rgba.Pointer, (nuint)rgba.ByteLength, Fixtures.JpegWidth, Fixtures.JpegHeight);
            Check(nativeResize == ManagedPipeline.ThumbnailFromRgba(rgba), "resize from RGBA: the widths differ");
            CheckDecodes(NativePipeline.EncodeJpeg(small.Pointer, (nuint)small.ByteLength, Fixtures.SmallWidth, Fixtures.SmallHeight), Fixtures.SmallWidth, "native JPEG encode");
        }

        CheckDecodes(ManagedPipeline.EncodeJpeg(small), Fixtures.SmallWidth, "managed JPEG encode");

        // What the PNG rows write, not only how fast: the deflate level trades one for the other.
        Console.WriteLine($"PNG 2MP round trip, bytes written: native {nativePng.Length:N0}, managed " +
            $"Optimal {ManagedPipeline.DecodeEncodePng(Fixtures.Png2Mp).Length:N0}, " +
            $"Fastest {ManagedPipeline.DecodeEncodePng(Fixtures.Png2Mp, System.IO.Compression.CompressionLevel.Fastest).Length:N0}, " +
            $"SmallestSize {ManagedPipeline.DecodeEncodePng(Fixtures.Png2Mp, System.IO.Compression.CompressionLevel.SmallestSize).Length:N0}.");
    }

    private static void CheckDecodes(byte[] encoded, int width, string what)
    {
        using var bitmap = SKBitmap.Decode(encoded);
        Check(bitmap is not null && bitmap.Width == width, $"{what}: SkiaSharp does not read back a {width}-pixel-wide image");
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException($"Verification failed: {what}.");
    }
}
