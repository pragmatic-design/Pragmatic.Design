using System.Globalization;
using System.Text;

// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     Compares an image with its reference and fails with something to look at: on a mismatch the actual,
///     the expected and an amplified difference image go to <see cref="VisualArtefacts.Directory" />, and the
///     message gives every metric measured, its bound, and the region where the two differ most.
/// </summary>
public static class VisualComparison
{
    /// <summary>The side of the square regions the worst one is chosen among.</summary>
    public const int RegionSize = 16;

    /// <param name="actual">The image the code under test produced.</param>
    /// <param name="expected">The reference, or the output of the independent implementation.</param>
    /// <param name="thresholds">The bounds to hold it to; at least one.</param>
    /// <param name="name">Names the images written on failure: unique per comparison, for example the test and the case.</param>
    /// <param name="outputDirectory">Where the images go on failure; <see cref="VisualArtefacts.Directory" /> when null.</param>
    public static void AssertSimilar(RgbaImage actual, RgbaImage expected, VisualThresholds thresholds, string name, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (thresholds.IsEmpty)
            throw new ArgumentException("A comparison with no threshold checks nothing: set at least one.", nameof(thresholds));
        if (actual.Width != expected.Width || actual.Height != expected.Height)
            throw new VisualMismatchException($"{name}: the actual image is {actual.Width} x {actual.Height}, the expected one {expected.Width} x {expected.Height}.");

        var failures = new List<string>();
        var measured = new List<string>();
        if (thresholds.MaxAbsolute is { } maxAbsolute)
        {
            var difference = PixelDifference.Measure(actual, expected).MaxColour;
            Record(measured, failures, $"largest absolute difference {difference}", $"at most {maxAbsolute}", difference <= maxAbsolute);
        }

        if (thresholds.MinPsnr is { } minPsnr)
        {
            var psnr = Psnr.Rgb(actual, expected);
            Record(measured, failures, $"PSNR {Format(psnr)} dB", $"at least {Format(minPsnr)} dB", psnr >= minPsnr);
        }

        if (thresholds.MinSsimLuma is { } minSsim)
        {
            var ssim = Ssim.Luma(actual, expected);
            Record(measured, failures, $"SSIM of luma {Format(ssim)}", $"at least {Format(minSsim)}", ssim >= minSsim);
        }

        if (thresholds.MaxMeanDeltaE is { } maxDeltaE)
        {
            var deltaE = DeltaE2000.Images(actual, expected).Mean;
            Record(measured, failures, $"mean CIEDE2000 {Format(deltaE)}", $"at most {Format(maxDeltaE)}", deltaE <= maxDeltaE);
        }

        if (failures.Count == 0)
            return;

        var (actualPath, expectedPath, differencePath) = VisualArtefacts.Write(name, actual, expected, outputDirectory);
        var (left, top, mean) = WorstRegion(actual, expected);
        var message = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"{name}: the image is not within its thresholds.")
            .AppendLine(CultureInfo.InvariantCulture, $"  failed: {string.Join("; ", failures)}")
            .AppendLine(CultureInfo.InvariantCulture, $"  measured: {string.Join("; ", measured)}")
            .AppendLine(CultureInfo.InvariantCulture, $"  worst {RegionSize} x {RegionSize} region: x {left}, y {top}, mean absolute difference {Format(mean)} per colour sample")
            .AppendLine(CultureInfo.InvariantCulture, $"  actual: {actualPath}")
            .AppendLine(CultureInfo.InvariantCulture, $"  expected: {expectedPath}")
            .Append(CultureInfo.InvariantCulture, $"  difference (x8): {differencePath}");
        throw new VisualMismatchException(message.ToString());
    }

    /// <summary>The <see cref="RegionSize" /> square (clipped at the right and bottom edges) with the largest mean absolute colour difference.</summary>
    public static (int Left, int Top, double MeanAbsolute) WorstRegion(RgbaImage actual, RgbaImage expected)
    {
        RgbaImage.RequireSameSize(actual, expected);
        (int, int, double) worst = (0, 0, -1);
        for (var top = 0; top < actual.Height; top += RegionSize)
        {
            for (var left = 0; left < actual.Width; left += RegionSize)
            {
                long sum = 0;
                var samples = 0;
                for (var y = top; y < Math.Min(top + RegionSize, actual.Height); y++)
                {
                    for (var x = left; x < Math.Min(left + RegionSize, actual.Width); x++)
                    {
                        var i = (y * actual.Width + x) * 4;
                        for (var c = 0; c < 3; c++)
                            sum += Math.Abs(actual.Pixels[i + c] - expected.Pixels[i + c]);
                        samples += 3;
                    }
                }

                var mean = (double)sum / samples;
                if (mean > worst.Item3)
                    worst = (left, top, mean);
            }
        }

        return worst;
    }

    private static void Record(List<string> measured, List<string> failures, string value, string bound, bool passed)
    {
        measured.Add(value);
        if (!passed)
            failures.Add($"{value}, expected {bound}");
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
