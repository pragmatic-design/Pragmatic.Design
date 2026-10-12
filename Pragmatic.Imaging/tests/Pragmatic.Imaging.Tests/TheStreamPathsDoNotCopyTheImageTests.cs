using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     The stream overloads read and write the encoded image without making managed copies of it.
/// </summary>
/// <remarks>
///     The measure is the managed bytes allocated on the calling thread during one call, the fewest
///     over five calls made after a warm-up call has filled the shared array pool. The decoded pixels live in native memory and
///     are not counted: what is counted is what these overloads add on top, which used to scale with
///     the encoded image (a 80 KB copy buffer, a growing <see cref="MemoryStream" />, a
///     <c>ToArray()</c> of the encoded result). Measured on a 17,462-byte PNG, before and after the
///     change: Load 99,680 → 184 bytes, FromStream 65,760 → 64, EncodeTo 17,528 → 40, and the QR
///     code to a stream 14,128 → 112. What is left is a few fixed objects, hence the 1 KB bound.
/// </remarks>
public sealed class TheStreamPathsDoNotCopyTheImageTests
{
    private const long Bound = 1024;

    private static readonly byte[] Png = TestHelper.CreateTestPng(1024, 768);

    [NativeRequiredFact]
    public void LoadFromStream_AllocatesNoCopyOfTheInput()
    {
        var allocated = Measure(() =>
        {
            using var input = new MemoryStream(Png, writable: false);
            using var pipeline = ImagePipeline.Load(input);
        });

        allocated.Fewest.Should().BeLessThan(Bound,
            $"the input is {Png.Length} bytes and should not be copied into managed memory (samples: {allocated.Samples})");
    }

    [NativeRequiredFact]
    public void InfoFromStream_AllocatesNoCopyOfTheHeader()
    {
        var allocated = Measure(() =>
        {
            using var input = new MemoryStream(Png, writable: false);
            _ = ImageInfo.FromStream(input);
        });

        allocated.Fewest.Should().BeLessThan(Bound, $"the header should not be copied into managed memory (samples: {allocated.Samples})");
    }

    [NativeRequiredFact]
    public void EncodeToStream_AllocatesNoCopyOfTheOutput()
    {
        using var pipeline = ImagePipeline.Load(Png);
        var output = new MemoryStream(capacity: 8 * 1024 * 1024);

        var allocated = Measure(() =>
        {
            output.SetLength(0);
            pipeline.EncodeTo(output, ImageFormat.Png);
        });

        output.Length.Should().BeGreaterThan(0);
        allocated.Fewest.Should().BeLessThan(Bound,
            $"the encoded image is {output.Length} bytes and should go to the stream without a managed copy (samples: {allocated.Samples})");
    }

    [NativeRequiredFact]
    public void QrCodeToStream_AllocatesNoCopyOfTheOutput()
    {
        var output = new MemoryStream(capacity: 1024 * 1024);

        var allocated = Measure(() =>
        {
            output.SetLength(0);
            QrCode.GeneratePng("https://example.com/a-reasonably-long-payload", output, moduleSize: 20);
        });

        output.Length.Should().BeGreaterThan(0);
        allocated.Fewest.Should().BeLessThan(Bound,
            $"the PNG is {output.Length} bytes and should go to the stream without a managed copy (samples: {allocated.Samples})");
    }

    /// <summary>
    ///     The fewest bytes one call allocated, over five calls after a warm-up, as text that carries
    ///     every sample for the failure message.
    /// </summary>
    /// <remarks>
    ///     A copy of the image happens on every call, so it is in every sample, the smallest included.
    ///     The minimum leaves out what the runtime allocates on the thread only now and then: the
    ///     gate, with eight suites running at once, once measured an <c>EncodeTo</c> call over the
    ///     bound that passed every run on its own.
    /// </remarks>
    private static (long Fewest, string Samples) Measure(Action call)
    {
        call();
        var samples = new long[5];
        for (var i = 0; i < samples.Length; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            call();
            samples[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return (samples.Min(), string.Join(", ", samples));
    }
}
