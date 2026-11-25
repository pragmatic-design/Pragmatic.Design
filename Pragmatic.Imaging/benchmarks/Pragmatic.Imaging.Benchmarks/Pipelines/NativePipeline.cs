using Pragmatic.Imaging.Benchmarks.Workload;

namespace Pragmatic.Imaging.Benchmarks.Pipelines;

/// <summary>The same stages on today's Pragmatic.Imaging, the Rust-backed one: the baseline.</summary>
public static class NativePipeline
{
    public static int Decode(byte[] encoded)
    {
        using var pipeline = ImagePipeline.Load(encoded);
        return (int)pipeline.Width;
    }

    public static int DecodeThumbnail(byte[] encoded)
    {
        using var pipeline = ImagePipeline.Load(encoded);
        pipeline.Thumbnail(Fixtures.ThumbnailWidth, Fixtures.ThumbnailHeight);
        return (int)pipeline.Width;
    }

    public static byte[] DecodeThumbnailEncodeJpeg(byte[] encoded)
    {
        using var pipeline = ImagePipeline.Load(encoded);
        return pipeline
            .Thumbnail(Fixtures.ThumbnailWidth, Fixtures.ThumbnailHeight)
            .Encode(ImageFormat.Jpeg, Fixtures.JpegQuality);
    }

    public static byte[] DecodeEncodePng(byte[] encoded)
    {
        using var pipeline = ImagePipeline.Load(encoded);
        return pipeline.Encode(ImageFormat.Png);
    }
}
