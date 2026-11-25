using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;
using SkiaSharp;

namespace Pragmatic.VisualTesting.Tests;

/// <summary>
///     A comparison that fails writes the actual, the expected and an amplified difference image, readable
///     by an independent decoder, and says in its message what failed, by how much, and where.
/// </summary>
/// <remarks>
///     On a synthetic image whose every sample is known (red = 2x, green = 2y, blue 100, on 128 x 128), so
///     a square raised by a few levels is raised by exactly that, never capped at 255.
/// </remarks>
public sealed class AFailedComparisonLeavesSomethingToLookAtTests : IDisposable
{
    private readonly string _output = Path.Combine(Path.GetTempPath(), "pragmatic-visual-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_output))
            Directory.Delete(_output, recursive: true);
    }

    [Fact]
    public void AssertSimilar_OnAnImageOutsideItsThresholds_WritesTheThreeImagesAndNamesTheWorstRegion()
    {
        var expected = Gradient();
        var actual = WithSquareRaised(expected, left: 64, top: 32, by: 40);

        var failure = Record(() => VisualComparison.AssertSimilar(actual, expected,
            new VisualThresholds { MinSsimLuma = 0.999, MaxAbsolute = 2 }, "square-raised", _output));

        (failure is VisualMismatchException).Should().BeTrue($"the comparison fails with its own exception, not {failure?.GetType().Name ?? "nothing"}");
        failure!.Message.Should().Contain("SSIM of luma");
        failure.Message.Should().Contain("expected at least 0.999");
        failure.Message.Should().Contain("largest absolute difference 40, expected at most 2");
        failure.Message.Should().Contain("region: x 64, y 32, mean absolute difference 40");
        foreach (var kind in new[] { "actual", "expected", "difference" })
        {
            var path = Path.Combine(_output, $"square-raised.{kind}.png");
            File.Exists(path).Should().BeTrue($"the {kind} image is what shows the failure");
            using var bitmap = SKBitmap.Decode(path);
            (bitmap is not null).Should().BeTrue($"SkiaSharp reads the {kind} image");
            bitmap!.Width.Should().Be(128);
        }

        using var read = SKBitmap.Decode(Path.Combine(_output, "square-raised.actual.png"));
        read.GetPixel(70, 40).Should().Be(new SKColor(2 * 70 + 40, 2 * 40 + 40, 140), "the actual image is written as it is");
        using var difference = SKBitmap.Decode(Path.Combine(_output, "square-raised.difference.png"));
        difference.GetPixel(70, 40).Should().Be(new SKColor(255, 255, 255), "40 levels amplified eight times saturate");
        difference.GetPixel(0, 0).Should().Be(new SKColor(0, 0, 0), "where nothing moved the difference is black");
    }

    [Fact]
    public void AssertSimilar_WithinItsThresholds_WritesNothing()
    {
        var image = Gradient();

        VisualComparison.AssertSimilar(image, image, VisualThresholds.Identical, "identical", _output);

        Directory.Exists(_output).Should().BeFalse();
    }

    [Fact]
    public void AssertSimilar_WithNoThreshold_IsRefused()
    {
        var image = Gradient();

        var failure = Record(() => VisualComparison.AssertSimilar(image, image, new VisualThresholds(), "none", _output));

        (failure is ArgumentException).Should().BeTrue($"a comparison that measures nothing is refused, not {failure?.GetType().Name ?? "passed"}");
    }

    [Fact]
    public void WorstRegion_OfOneChangedSquare_IsThatSquare()
    {
        var expected = Gradient();
        var actual = WithSquareRaised(expected, left: 96, top: 16, by: 3);

        VisualComparison.WorstRegion(actual, expected).Should().Be((96, 16, 3.0));
    }

    private static RgbaImage Gradient()
    {
        var pixels = new byte[128 * 128 * 4];
        for (var y = 0; y < 128; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                var i = (y * 128 + x) * 4;
                pixels[i] = (byte)(2 * x);
                pixels[i + 1] = (byte)(2 * y);
                pixels[i + 2] = 100;
                pixels[i + 3] = 255;
            }
        }

        return new RgbaImage(128, 128, pixels);
    }

    /// <summary>A copy with every colour sample of one 16 x 16 square raised by <paramref name="by" />.</summary>
    private static RgbaImage WithSquareRaised(RgbaImage image, int left, int top, int by)
    {
        var pixels = (byte[])image.Pixels.Clone();
        for (var y = top; y < top + VisualComparison.RegionSize; y++)
        {
            for (var x = left; x < left + VisualComparison.RegionSize; x++)
            {
                var i = (y * image.Width + x) * 4;
                for (var c = 0; c < 3; c++)
                    pixels[i + c] = (byte)(pixels[i + c] + by);
            }
        }

        return new RgbaImage(image.Width, image.Height, pixels);
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
