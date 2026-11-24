using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     A server's upload path, in cumulative stages: decode a 4 MP JPEG; decode and thumbnail it into a
///     400 × 300 box; decode, thumbnail and encode the result as JPEG. And the PNG round trip: decode a
///     2 MP PNG and encode it again. Then two stages on their own: the resize from raw pixels, and the
///     JPEG encode of a thumbnail-sized image.
/// </summary>
/// <remarks>
///     The first four compare the managed codecs with today's Rust-backed Pragmatic.Imaging, the
///     baseline. The stages are cumulative because the native pipeline has no public entry from raw
///     pixels: the cost of one stage is the difference between two rows, for both engines alike. The last
///     two have no native row for the same reason. Both engines run single-threaded.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class UploadPathBenchmarks
{
    private byte[] _jpeg = null!;
    private byte[] _png = null!;
    private PixelBuffer _rgbaBuffer = null!;
    private PixelBuffer _smallBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _jpeg = Fixtures.Jpeg4Mp;
        _png = Fixtures.Png2Mp;
        _rgbaBuffer = new PixelBuffer(Fixtures.JpegWidth, Fixtures.JpegHeight);
        Fixtures.Rgba4Mp.CopyTo(_rgbaBuffer.Pixels);
        _smallBuffer = new PixelBuffer(Fixtures.SmallWidth, Fixtures.SmallHeight);
        Fixtures.RgbaSmall.CopyTo(_smallBuffer.Pixels);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rgbaBuffer.Dispose();
        _smallBuffer.Dispose();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("1 Decode JPEG 4MP")]
    public int Decode_Native() => NativePipeline.Decode(_jpeg);

    [Benchmark, BenchmarkCategory("1 Decode JPEG 4MP")]
    public int Decode_Managed() => ManagedPipeline.Decode(_jpeg);

    [Benchmark(Baseline = true), BenchmarkCategory("2 Decode+Thumbnail")]
    public int Thumbnail_Native() => NativePipeline.DecodeThumbnail(_jpeg);

    [Benchmark, BenchmarkCategory("2 Decode+Thumbnail")]
    public int Thumbnail_Managed() => ManagedPipeline.DecodeThumbnail(_jpeg);

    [Benchmark(Baseline = true), BenchmarkCategory("3 Decode+Thumbnail+Encode JPEG")]
    public byte[] Pipeline_Native() => NativePipeline.DecodeThumbnailEncodeJpeg(_jpeg);

    [Benchmark, BenchmarkCategory("3 Decode+Thumbnail+Encode JPEG")]
    public byte[] Pipeline_Managed() => ManagedPipeline.DecodeThumbnailEncodeJpeg(_jpeg);

    [Benchmark(Baseline = true), BenchmarkCategory("4 PNG 2MP decode+encode")]
    public byte[] Png_Native() => NativePipeline.DecodeEncodePng(_png);

    [Benchmark, BenchmarkCategory("4 PNG 2MP decode+encode")]
    public byte[] Png_Managed() => ManagedPipeline.DecodeEncodePng(_png);

    // The native encoder writes a larger file: little compression. This row is the managed encoder at
    // its fastest zlib level, the comparison the time alone cannot make.
    [Benchmark, BenchmarkCategory("4 PNG 2MP decode+encode")]
    public byte[] Png_Managed_Fastest() => ManagedPipeline.DecodeEncodePng(_png, System.IO.Compression.CompressionLevel.Fastest);

    [Benchmark(Baseline = true), BenchmarkCategory("5 Thumbnail from RGBA 4MP")]
    public int Resize_Managed() => ManagedPipeline.ThumbnailFromRgba(_rgbaBuffer);

    [Benchmark(Baseline = true), BenchmarkCategory("6 Encode JPEG 400x267 q85")]
    public byte[] Encode_Managed() => ManagedPipeline.EncodeJpeg(_smallBuffer);

    /// <summary>The standard tables in one pass, as libjpeg-turbo's default does.</summary>
    [Benchmark, BenchmarkCategory("6 Encode JPEG 400x267 q85")]
    public byte[] Encode_Managed_StandardTables() => ManagedPipeline.EncodeJpeg(_smallBuffer, optimizeCoding: false);
}
