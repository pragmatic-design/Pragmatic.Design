using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;
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
        CheckDecodes(NativePipeline.DecodeEncodePng(Fixtures.Png2Mp), Fixtures.PngWidth, "native PNG round trip");
        CheckDecodes(ManagedPipeline.DecodeEncodePng(Fixtures.Png2Mp), Fixtures.PngWidth, "managed PNG round trip");
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
