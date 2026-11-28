using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     TIFF, managed against native: decode a photograph Pillow wrote in LZW (scikit-image's coffee, 203 x 151 RGB),
///     the 2 MP image as the native encoder writes it (uncompressed RGBA), libtiff's large one-bit LZW image
///     (7795 x 3122) and its Group 4 fax (2453 x 3369); encode the 2 MP image from pixels already in memory.
/// </summary>
/// <remarks>SkiaSharp reads no TIFF, so the comparison is with the native library alone.</remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TiffBenchmarks
{
    private byte[] _photo = null!;
    private byte[] _tiff2Mp = null!;
    private byte[] _bilevel = null!;
    private byte[] _fax = null!;
    private PixelBuffer _image = null!;

    [GlobalSetup]
    public unsafe void Setup()
    {
        var references = Path.Combine(AppContext.BaseDirectory, "References", "Tiff");
        _photo = File.ReadAllBytes(Path.Combine(references, "coffee-rgb-tiff_lzw.tiff"));
        _bilevel = File.ReadAllBytes(Path.Combine(references, "lzw-single-strip.tiff"));
        _fax = File.ReadAllBytes(Path.Combine(references, "testfax4.tiff"));
        _image = new PixelBuffer(Fixtures.PngWidth, Fixtures.PngHeight);
        Fixtures.Rgba2Mp.CopyTo(_image.Pixels);
        _tiff2Mp = NativePipeline.EncodeTiff(_image.Pointer, (nuint)_image.ByteLength, Fixtures.PngWidth, Fixtures.PngHeight);
    }

    [GlobalCleanup]
    public void Cleanup() => _image.Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("Decode TIFF LZW photo")]
    public int DecodeTiffPhoto_Native() => NativePipeline.Decode(_photo);

    [Benchmark, BenchmarkCategory("Decode TIFF LZW photo")]
    public int DecodeTiffPhoto_Managed() => ManagedPipeline.DecodeTiff(_photo);

    [Benchmark(Baseline = true), BenchmarkCategory("Decode TIFF 2MP")]
    public int DecodeTiff2Mp_Native() => NativePipeline.Decode(_tiff2Mp);

    [Benchmark, BenchmarkCategory("Decode TIFF 2MP")]
    public int DecodeTiff2Mp_Managed() => ManagedPipeline.DecodeTiff(_tiff2Mp);

    [Benchmark(Baseline = true), BenchmarkCategory("Decode TIFF bilevel LZW")]
    public int DecodeTiffBilevel_Native() => NativePipeline.Decode(_bilevel);

    [Benchmark, BenchmarkCategory("Decode TIFF bilevel LZW")]
    public int DecodeTiffBilevel_Managed() => ManagedPipeline.DecodeTiff(_bilevel);

    [Benchmark(Baseline = true), BenchmarkCategory("Decode TIFF Group 4")]
    public int DecodeTiffFax_Native() => NativePipeline.Decode(_fax);

    [Benchmark, BenchmarkCategory("Decode TIFF Group 4")]
    public int DecodeTiffFax_Managed() => ManagedPipeline.DecodeTiff(_fax);

    [Benchmark(Baseline = true), BenchmarkCategory("Encode TIFF 2MP")]
    public unsafe byte[] EncodeTiff2Mp_Native() => NativePipeline.EncodeTiff(_image.Pointer, (nuint)_image.ByteLength, Fixtures.PngWidth, Fixtures.PngHeight);

    [Benchmark, BenchmarkCategory("Encode TIFF 2MP")]
    public byte[] EncodeTiff2Mp_Managed() => ManagedPipeline.EncodeTiff(_image);
}
