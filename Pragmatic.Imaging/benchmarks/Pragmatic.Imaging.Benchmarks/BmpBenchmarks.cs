using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     BMP, managed against native: decode the 2 MP image as a 24-bit file and as the 32-bit one with alpha
///     both encoders write, and encode it from pixels already in memory.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BmpBenchmarks
{
    private byte[] _bmp24 = null!;
    private byte[] _bmp32 = null!;
    private PixelBuffer _image = null!;

    [GlobalSetup]
    public void Setup()
    {
        _bmp24 = Fixtures.Bmp24Of2Mp;
        _bmp32 = Fixtures.Bmp32Of2Mp;
        _image = new PixelBuffer(Fixtures.PngWidth, Fixtures.PngHeight);
        Fixtures.Rgba2Mp.CopyTo(_image.Pixels);
    }

    [GlobalCleanup]
    public void Cleanup() => _image.Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("Decode BMP 24-bit 2MP")]
    public int DecodeBmp24_Native() => NativePipeline.Decode(_bmp24);

    [Benchmark, BenchmarkCategory("Decode BMP 24-bit 2MP")]
    public int DecodeBmp24_Managed() => ManagedPipeline.DecodeBmp(_bmp24);

    [Benchmark(Baseline = true), BenchmarkCategory("Decode BMP 32-bit 2MP")]
    public int DecodeBmp32_Native() => NativePipeline.Decode(_bmp32);

    [Benchmark, BenchmarkCategory("Decode BMP 32-bit 2MP")]
    public int DecodeBmp32_Managed() => ManagedPipeline.DecodeBmp(_bmp32);

    [Benchmark(Baseline = true), BenchmarkCategory("Encode BMP 2MP")]
    public unsafe byte[] EncodeBmp_Native() => NativePipeline.EncodeBmp(_image.Pointer, (nuint)_image.ByteLength, Fixtures.PngWidth, Fixtures.PngHeight);

    [Benchmark, BenchmarkCategory("Encode BMP 2MP")]
    public byte[] EncodeBmp_Managed() => ManagedPipeline.EncodeBmp(_image);
}
