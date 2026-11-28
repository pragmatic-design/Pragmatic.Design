using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     GIF, managed against native: decode a photograph Pillow wrote (scikit-image's coffee, 203 x 151 in 256
///     colours) and the 2 MP image as the native encoder writes it, and encode the photograph (600 x 400) and the
///     2 MP image from pixels already in memory, which both encoders reduce to 256 colours.
/// </summary>
/// <remarks>
///     SkiaSharp writes no GIF. The 2 MP decode reads the native encoder's file: the cost measured is the LZW and
///     the colour table of a full 256-colour frame, which does not depend on whose palette it is.
///     What either encoder allocates is the file. On the 2 MP image the managed file is about 18% larger than the
///     native one and 5 dB closer to the source (PSNR 38.0 against 32.9 dB): the refined palette keeps detail that
///     NeuQuant's drops, and the detail costs LZW bytes. On the photograph the managed file is the smaller.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GifBenchmarks
{
    private byte[] _photoGif = null!;
    private byte[] _gif2Mp = null!;
    private PixelBuffer _image = null!;
    private PixelBuffer _photo = null!;

    [GlobalSetup]
    public unsafe void Setup()
    {
        _photoGif = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "References", "Gif", "coffee.gif"));
        _image = new PixelBuffer(Fixtures.PngWidth, Fixtures.PngHeight);
        Fixtures.Rgba2Mp.CopyTo(_image.Pixels);
        _photo = new PixelBuffer(Fixtures.PhotoWidth, Fixtures.PhotoHeight);
        Fixtures.RgbaPhoto.CopyTo(_photo.Pixels);
        _gif2Mp = NativePipeline.EncodeGif(_image.Pointer, (nuint)_image.ByteLength, Fixtures.PngWidth, Fixtures.PngHeight);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _image.Dispose();
        _photo.Dispose();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Decode GIF photo")]
    public int DecodeGifPhoto_Native() => NativePipeline.Decode(_photoGif);

    [Benchmark, BenchmarkCategory("Decode GIF photo")]
    public int DecodeGifPhoto_Managed() => ManagedPipeline.DecodeGif(_photoGif);

    [Benchmark(Baseline = true), BenchmarkCategory("Decode GIF 2MP")]
    public int DecodeGif2Mp_Native() => NativePipeline.Decode(_gif2Mp);

    [Benchmark, BenchmarkCategory("Decode GIF 2MP")]
    public int DecodeGif2Mp_Managed() => ManagedPipeline.DecodeGif(_gif2Mp);

    [Benchmark(Baseline = true), BenchmarkCategory("Encode GIF photo 600x400")]
    public unsafe byte[] EncodeGifPhoto_Native() => NativePipeline.EncodeGif(_photo.Pointer, (nuint)_photo.ByteLength, Fixtures.PhotoWidth, Fixtures.PhotoHeight);

    [Benchmark, BenchmarkCategory("Encode GIF photo 600x400")]
    public byte[] EncodeGifPhoto_Managed() => ManagedPipeline.EncodeGif(_photo);

    [Benchmark(Baseline = true), BenchmarkCategory("Encode GIF 2MP")]
    public unsafe byte[] EncodeGif2Mp_Native() => NativePipeline.EncodeGif(_image.Pointer, (nuint)_image.ByteLength, Fixtures.PngWidth, Fixtures.PngHeight);

    [Benchmark, BenchmarkCategory("Encode GIF 2MP")]
    public byte[] EncodeGif2Mp_Managed() => ManagedPipeline.EncodeGif(_image);
}
