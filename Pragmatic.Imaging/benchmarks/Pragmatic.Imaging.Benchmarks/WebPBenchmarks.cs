using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     WebP: the managed codec against the Rust library it replaced (image-webp, through the native
///     pipeline). Decode a lossy 4 MP photo and a lossless 2 MP image that libwebp wrote; encode the 2 MP
///     image lossless, from pixels already in memory.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WebPBenchmarks
{
    private byte[] _lossy = null!;
    private byte[] _lossless = null!;
    private PixelBuffer _image = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lossy = Fixtures.WebPLossy4Mp;
        _lossless = Fixtures.WebPLossless2Mp;
        _image = new PixelBuffer(Fixtures.PngWidth, Fixtures.PngHeight);
        Fixtures.Rgba2Mp.CopyTo(_image.Pixels);
    }

    [GlobalCleanup]
    public void Cleanup() => _image.Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("Decode WebP lossy 4MP")]
    public int DecodeWebPLossy_Native() => NativePipeline.Decode(_lossy);

    [Benchmark, BenchmarkCategory("Decode WebP lossy 4MP")]
    public int DecodeWebPLossy_Managed() => ManagedPipeline.DecodeWebP(_lossy);

    [Benchmark(Baseline = true), BenchmarkCategory("Decode WebP lossless 2MP")]
    public int DecodeWebPLossless_Native() => NativePipeline.Decode(_lossless);

    [Benchmark, BenchmarkCategory("Decode WebP lossless 2MP")]
    public int DecodeWebPLossless_Managed() => ManagedPipeline.DecodeWebP(_lossless);

    [Benchmark(Baseline = true), BenchmarkCategory("Encode WebP lossless 2MP")]
    public unsafe byte[] EncodeWebPLossless_Native() => NativePipeline.EncodeWebP(_image.Pointer, (nuint)_image.ByteLength, Fixtures.PngWidth, Fixtures.PngHeight);

    [Benchmark, BenchmarkCategory("Encode WebP lossless 2MP")]
    public byte[] EncodeWebPLossless_Managed() => ManagedPipeline.EncodeWebP(_image);

    // The native library never wrote lossy WebP: the reference is libwebp, through SkiaSharp, which runs
    // it at method 3.
    [Benchmark(Baseline = true), BenchmarkCategory("Encode WebP lossy 2MP")]
    public byte[] EncodeWebPLossy_LibWebP() => LibWebP.EncodeLossy(_image, LossyQuality);

    [Benchmark, BenchmarkCategory("Encode WebP lossy 2MP")]
    public byte[] EncodeWebPLossy_Managed() => Codecs.WebP.WebPEncoder.EncodeLossy(_image, LossyQuality);

    /// <summary>libwebp's default quality.</summary>
    private const int LossyQuality = 75;
}
