using Pragmatic.Imaging.Benchmarks;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     Every imaging benchmark row the allocation ratchet gives a one-off allowance allocates, on the
///     calling thread, exactly what it did when the allowance was given.
/// </summary>
/// <remarks>
///     The ratchet reads BenchmarkDotNet's count, which takes in every thread of the process, and these rows
///     measure so few operations that another thread's allocation during one measurement moved a row by
///     kilobytes (#1011). <c>ONE_OFF_BYTES</c> in <c>scripts/benchmarks.mjs</c> lets that through, and with
///     it any regression smaller than the allowance over the operations measured. This test is what still
///     sees those: it calls the benchmark methods themselves and counts only this thread, the fewest bytes
///     over five calls after a warm-up one, as <see cref="TheStreamPathsDoNotCopyTheImageTests" /> does.
///     A change to what a row allocates changes its number here and in the baseline together.
/// </remarks>
public sealed class TheBenchmarkedOperationsAllocateWhatTheyDidTests : IDisposable
{
#if NET11_0_OR_GREATER
    private const bool OnNet11 = true;
#else
    private const bool OnNet11 = false;
#endif

    private readonly OperationBenchmarks _operations = new();
    private readonly UploadPathBenchmarks _uploads = new();
    private readonly WebPBenchmarks _webp = new();

    public TheBenchmarkedOperationsAllocateWhatTheyDidTests()
    {
        _operations.Setup();
        _uploads.Setup();
        _webp.Setup();
    }

    public void Dispose()
    {
        _operations.Cleanup();
        _uploads.Cleanup();
        _webp.Cleanup();
    }

    [Fact]
    public void BlurManaged_OneCall_AllocatesTheResultAndTheKernel() => Expect(104, _operations.Blur_Managed);

    [NativeRequiredFact]
    public void BlurNative_OneCall_AllocatesNothingManaged() => Expect(0, _operations.Blur_Native);

    [Fact]
    public void BrightnessManaged_OneCall_AllocatesNothing() => Expect(0, _operations.Brightness_Managed);

    [NativeRequiredFact]
    public void BrightnessNative_OneCall_AllocatesNothingManaged() => Expect(0, _operations.Brightness_Native);

    [Fact]
    public void ContrastManaged_OneCall_AllocatesNothing() => Expect(0, _operations.Contrast_Managed);

    [NativeRequiredFact]
    public void ContrastNative_OneCall_AllocatesNothingManaged() => Expect(0, _operations.Contrast_Native);

    [NativeRequiredFact]
    public void CropNative_OneCall_AllocatesNothingManaged() => Expect(0, _operations.Crop_Native);

    [Fact]
    public void FlipHorizontalManaged_OneCall_AllocatesNothing() => Expect(0, _operations.FlipHorizontal_Managed);

    [NativeRequiredFact]
    public void FlipHorizontalNative_OneCall_AllocatesNothingManaged() => Expect(0, _operations.FlipHorizontal_Native);

    [Fact]
    public void GrayscaleManaged_OneCall_AllocatesNothing() => Expect(0, _operations.Grayscale_Managed);

    [NativeRequiredFact]
    public void GrayscaleNative_OneCall_AllocatesNothingManaged() => Expect(0, _operations.Grayscale_Native);

    [Fact]
    public void Rotate90Managed_OneCall_AllocatesTheResult() => Expect(32, _operations.Rotate90_Managed);

    [NativeRequiredFact]
    public void Rotate90Native_OneCall_AllocatesNothingManaged() => Expect(0, _operations.Rotate90_Native);

    [Fact]
    public void SharpenManaged_OneCall_AllocatesTheResultAndTheKernel() => Expect(80, _operations.Sharpen_Managed);

    [NativeRequiredFact]
    public void SharpenNative_OneCall_AllocatesNothingManaged() => Expect(0, _operations.Sharpen_Native);

    // The managed decode is the one path whose count depends on the build: in this suite's Debug build it
    // allocates more than the runner's Release one measures (1,504 B for the decode, 36,472 with the
    // thumbnail, 100,560 with the encode), and 24 B less on .NET 11 than on .NET 10. The numbers here are
    // this build's; the baseline keeps the runner's.
    [Fact]
    public void DecodeManaged_OneCall_AllocatesWhatItDid() => Expect(OnNet11 ? 1544 : 1568, () => _uploads.Decode_Managed());

    [NativeRequiredFact]
    public void DecodeNative_OneCall_AllocatesOneWrapper() => Expect(40, () => _uploads.Decode_Native());

    [NativeRequiredFact]
    public void EncodeNative_OneCall_AllocatesTheEncodedImage() => Expect(15088, () => _uploads.Encode_Native());

    [Fact]
    public void PipelineManaged_OneCall_AllocatesWhatItDid() => Expect(OnNet11 ? 100816 : 100840, () => _uploads.Pipeline_Managed());

    [NativeRequiredFact]
    public void PipelineNative_OneCall_AllocatesTwoWrappersAndTheEncodedImage() => Expect(20096, () => _uploads.Pipeline_Native());

    [Fact]
    public void ResizeManaged_OneCall_AllocatesWhatItDid() => Expect(35032, () => _uploads.Resize_Managed());

    [NativeRequiredFact]
    public void ResizeNative_OneCall_AllocatesOneWrapper() => Expect(40, () => _uploads.Resize_Native());

    [Fact]
    public void ThumbnailManaged_OneCall_AllocatesWhatItDid() => Expect(OnNet11 ? 36584 : 36608, () => _uploads.Thumbnail_Managed());

    [NativeRequiredFact]
    public void ThumbnailNative_OneCall_AllocatesTwoWrappers() => Expect(80, () => _uploads.Thumbnail_Native());

    // The image's wrapper and nothing else: the decoder keeps its reader, lists and code tables on the thread.
    [Fact]
    public void DecodeWebPLosslessManaged_OneCall_AllocatesOneWrapper() => Expect(32, () => _webp.DecodeWebPLossless_Managed());

    [NativeRequiredFact]
    public void DecodeWebPLosslessNative_OneCall_AllocatesOneWrapper() => Expect(40, () => _webp.DecodeWebPLossless_Native());

    // The image's wrapper (32 B in a Release build): the decoder keeps its partitions, header, frame and
    // reconstructor on the thread. In this Debug build each read of the quantizer step tables (a
    // ReadOnlySpan<short>) allocates 72 B more: six per segment, four segments, 1,728 B, where optimized code
    // allocates nothing for them.
    [Fact]
    public void DecodeWebPLossyManaged_OneCall_AllocatesWhatItDid() => Expect(1760, () => _webp.DecodeWebPLossy_Managed());

    [NativeRequiredFact]
    public void DecodeWebPLossyNative_OneCall_AllocatesOneWrapper() => Expect(40, () => _webp.DecodeWebPLossy_Native());

    [Fact]
    public void EncodeWebPLosslessManaged_OneCall_AllocatesTheEncodedImage() => Expect(1497832, () => _webp.EncodeWebPLossless_Managed());

    [NativeRequiredFact]
    public void EncodeWebPLosslessNative_OneCall_AllocatesTheEncodedImage() => Expect(1498816, () => _webp.EncodeWebPLossless_Native());

    // The file (19,224 B in a Release build): the encoder keeps its working state on the thread, and takes
    // the planes, tokens and partitions from the pool. In this Debug build each read of the quantizer step
    // tables (a ReadOnlySpan<short>) allocates 72 B more, twelve reads an encode, where optimized code
    // allocates nothing for them.
    [Fact]
    public void EncodeWebPLossyManaged_OneCall_AllocatesWhatItDid() => Expect(20088, () => _webp.EncodeWebPLossy_Managed());

    [Fact]
    public void EncodeWebPLossyLibWebP_OneCall_AllocatesTheEncodedImage() => Expect(19504, () => _webp.EncodeWebPLossy_LibWebP());

    // The same on the photograph and the thumbnail-sized photo: the file, and the 864 B of this Debug build.
    [Fact]
    public void EncodeWebPLossyPhotoManaged_OneCall_AllocatesWhatItDid() => Expect(32192, () => _webp.EncodeWebPLossyPhoto_Managed());

    [Fact]
    public void EncodeWebPLossyPhotoLibWebP_OneCall_AllocatesTheEncodedImage() => Expect(31632, () => _webp.EncodeWebPLossyPhoto_LibWebP());

    [Fact]
    public void EncodeWebPLossySmallManaged_OneCall_AllocatesWhatItDid() => Expect(2976, () => _webp.EncodeWebPLossySmall_Managed());

    [Fact]
    public void EncodeWebPLossySmallLibWebP_OneCall_AllocatesTheEncodedImage() => Expect(2448, () => _webp.EncodeWebPLossySmall_LibWebP());

    private static void Expect(long bytes, Action call)
    {
        call();
        var samples = new long[5];
        for (var i = 0; i < samples.Length; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            call();
            samples[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        samples.Min().Should().Be(bytes, $"the fewest bytes over five calls on this thread (samples: {string.Join(", ", samples)})");
    }
}
