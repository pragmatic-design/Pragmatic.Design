using System.Globalization;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.VisualTesting.Tests;

/// <summary>
///     Every metric gives, on the committed image pairs, what an independent implementation gives:
///     scikit-image and NumPy for the pixel metrics (<c>References/metrics.json</c>, with the versions that
///     computed it), and the 34 pairs Sharma, Wu and Dalal published for CIEDE2000.
/// </summary>
/// <remarks>
///     The tolerances are the arithmetic, not the method: summing in another order moves a mean of
///     doubles in its last digits, and Sharma's table is rounded to four decimals.
/// </remarks>
public sealed class TheMetricsMatchTheirIndependentImplementationsTests
{
    private const double Arithmetic = 1e-9;
    private const double SharmaRounding = 0.5e-4 + 1e-9;

    public static TheoryData<string> Pairs()
    {
        var data = new TheoryData<string>();
        foreach (var pair in Metrics().RootElement.GetProperty("pairs").EnumerateObject())
            data.Add(pair.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryMetric_OnACommittedPair_IsWhatScikitImageComputed(string pair)
    {
        using var metrics = Metrics();
        var expected = metrics.RootElement.GetProperty("pairs").GetProperty(pair);
        var files = pair.Split('|');
        var reference = Fixtures.Load(files[0]);
        var distorted = Fixtures.Load(files[1]);

        var difference = PixelDifference.Measure(distorted, reference);
        for (var c = 0; c < 3; c++)
        {
            difference.MaxAbsolute[c].Should().Be(expected.GetProperty("maxAbsolute")[c].GetInt32(), $"the largest difference of channel {c}");
            difference.MeanAbsolute[c].Should().BeApproximately(expected.GetProperty("meanAbsolute")[c].GetDouble(), Arithmetic, $"the mean difference of channel {c}");
            Ssim.Channel(distorted, reference, c).Should().BeApproximately(expected.GetProperty("ssimChannels")[c].GetDouble(), Arithmetic, $"the SSIM of channel {c}");
        }

        Psnr.Rgb(distorted, reference).Should().BeApproximately(expected.GetProperty("psnr").GetDouble(), Arithmetic);
        Ssim.Luma(distorted, reference).Should().BeApproximately(expected.GetProperty("ssimLuma").GetDouble(), Arithmetic);
        var (mean, max) = DeltaE2000.Images(reference, distorted);
        mean.Should().BeApproximately(expected.GetProperty("deltaE2000Mean").GetDouble(), Arithmetic);
        max.Should().BeApproximately(expected.GetProperty("deltaE2000Max").GetDouble(), Arithmetic);
    }

    public static TheoryData<int, double, double, double, double, double, double, double> SharmaPairs()
    {
        var data = new TheoryData<int, double, double, double, double, double, double, double>();
        foreach (var line in File.ReadLines(Path.Combine(Fixtures.Directory, "ciede2000_test_data.txt")))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
                continue;
            // pair, "1", L1 a1 b1, ten intermediate values, dE, "2", L2 a2 b2, …
            var v = line.Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            double D(int i) => double.Parse(v[i], CultureInfo.InvariantCulture);
            data.Add(int.Parse(v[0], CultureInfo.InvariantCulture), D(2), D(3), D(4), D(17), D(18), D(19), D(15));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SharmaPairs))]
    public void DeltaE2000_OnSharmasTestPair_IsThePublishedValue(int pair, double l1, double a1, double b1, double l2, double a2, double b2, double published)
    {
        DeltaE2000.Between(new CieLab(l1, a1, b1), new CieLab(l2, a2, b2))
            .Should().BeApproximately(published, SharmaRounding, $"pair {pair} of Sharma, Wu and Dalal's table");
    }

    [Fact]
    public void SharmasTable_AsCommitted_HasItsThirtyFourPairs() => SharmaPairs().Count.Should().Be(34);

    [Fact]
    public void Ssim_OfAnImageWithItself_IsOne()
    {
        var image = Fixtures.Load("coffee-texture.original.png");

        Ssim.Luma(image, image).Should().Be(1.0);
        Psnr.Rgb(image, image).Should().Be(double.PositiveInfinity);
    }

    private static JsonDocument Metrics() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Directory, "metrics.json")));
}
