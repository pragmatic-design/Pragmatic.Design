using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Transforms;
using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     Each pipeline operation on the 4 MP photo already decoded: the managed operation Pragmatic.Imaging
///     runs now against the Rust function it ran before, on the same pixels.
/// </summary>
/// <remarks>
///     The engines do not do the same work, and that is the point of measuring both. The native library
///     copies its input into a new buffer for every operation and the pipeline frees the old one; the
///     managed flips, greyscale, brightness and contrast change the pixels in place, and the rest write a
///     new buffer from the pool. Each row is what one call costs the pipeline with that engine.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class OperationBenchmarks
{
    private const uint Width = Fixtures.JpegWidth;
    private const uint Height = Fixtures.JpegHeight;

    private PixelBuffer _image = null!;

    [GlobalSetup]
    public void Setup()
    {
        _image = new PixelBuffer(Fixtures.JpegWidth, Fixtures.JpegHeight);
        Fixtures.Rgba4Mp.CopyTo(_image.Pixels);
    }

    [GlobalCleanup]
    public void Cleanup() => _image.Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("Rotate 90")]
    public void Rotate90_Native() => Free(Native((p, l, o, ol) =>
    {
        uint w, h;
        return NativeImports.pragmatic_image_rotate(p, l, Width, Height, 90, o, ol, &w, &h);
    }));

    [Benchmark, BenchmarkCategory("Rotate 90")]
    public void Rotate90_Managed() => PixelGeometry.Rotate90(_image).Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("Flip horizontal")]
    public void FlipHorizontal_Native() => Free(Native((p, l, o, ol) => NativeImports.pragmatic_image_flip(p, l, Width, Height, 1, o, ol)));

    [Benchmark, BenchmarkCategory("Flip horizontal")]
    public void FlipHorizontal_Managed() => PixelGeometry.FlipHorizontal(_image);

    [Benchmark(Baseline = true), BenchmarkCategory("Crop the centre quarter")]
    public void Crop_Native() => Free(Native((p, l, o, ol) => NativeImports.pragmatic_image_crop(p, l, Width, Height, Width / 4, Height / 4, Width / 2, Height / 2, o, ol)));

    [Benchmark, BenchmarkCategory("Crop the centre quarter")]
    public void Crop_Managed() => PixelGeometry.Crop(_image, (int)Width / 4, (int)Height / 4, (int)Width / 2, (int)Height / 2).Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("Grayscale")]
    public void Grayscale_Native() => Free(Native((p, l, o, ol) => NativeImports.pragmatic_image_grayscale(p, l, Width, Height, o, ol)));

    [Benchmark, BenchmarkCategory("Grayscale")]
    public void Grayscale_Managed() => PixelAdjustments.Grayscale(_image);

    [Benchmark(Baseline = true), BenchmarkCategory("Brightness +30")]
    public void Brightness_Native() => Free(Native((p, l, o, ol) => NativeImports.pragmatic_image_brightness(p, l, Width, Height, 30, o, ol)));

    [Benchmark, BenchmarkCategory("Brightness +30")]
    public void Brightness_Managed() => PixelAdjustments.Brightness(_image, 30);

    [Benchmark(Baseline = true), BenchmarkCategory("Contrast +20")]
    public void Contrast_Native() => Free(Native((p, l, o, ol) => NativeImports.pragmatic_image_contrast(p, l, Width, Height, 20f, o, ol)));

    [Benchmark, BenchmarkCategory("Contrast +20")]
    public void Contrast_Managed() => PixelAdjustments.Contrast(_image, 20f);

    [Benchmark(Baseline = true), BenchmarkCategory("Blur sigma 2")]
    public void Blur_Native() => Free(Native((p, l, o, ol) => NativeImports.pragmatic_image_blur(p, l, Width, Height, 2f, o, ol)));

    [Benchmark, BenchmarkCategory("Blur sigma 2")]
    public void Blur_Managed() => GaussianBlur.Blur(_image, 2f).Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("Sharpen sigma 1")]
    public void Sharpen_Native() => Free(Native((p, l, o, ol) => NativeImports.pragmatic_image_sharpen(p, l, Width, Height, 1f, 1, o, ol)));

    [Benchmark, BenchmarkCategory("Sharpen sigma 1")]
    public void Sharpen_Managed() => GaussianBlur.Unsharpen(_image, 1f, 1).Dispose();

    private delegate int NativeOperation(byte* rgba, nuint length, nint* output, nuint* outputLength);

    private (nint Pointer, nuint Length) Native(NativeOperation operation)
    {
        nint output;
        nuint length;
        NativeError.CheckResult(operation(_image.Pointer, (nuint)_image.ByteLength, &output, &length), "benchmark");
        return (output, length);
    }

    private static void Free((nint Pointer, nuint Length) buffer) => NativeImports.pragmatic_free_buffer(buffer.Pointer, buffer.Length);
}
