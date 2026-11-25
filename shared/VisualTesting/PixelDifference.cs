// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     The absolute difference between two images, per channel: the largest and the mean, over R, G, B
///     and A in that order.
/// </summary>
public sealed record PixelDifference(int[] MaxAbsolute, double[] MeanAbsolute)
{
    public static PixelDifference Measure(RgbaImage actual, RgbaImage expected)
    {
        RgbaImage.RequireSameSize(actual, expected);
        var max = new int[4];
        var sum = new long[4];
        var a = actual.Pixels;
        var e = expected.Pixels;
        for (var i = 0; i < a.Length; i++)
        {
            var c = i & 3;
            var d = Math.Abs(a[i] - e[i]);
            sum[c] += d;
            if (d > max[c])
                max[c] = d;
        }

        var pixels = (double)actual.Width * actual.Height;
        return new PixelDifference(max, [sum[0] / pixels, sum[1] / pixels, sum[2] / pixels, sum[3] / pixels]);
    }

    /// <summary>The largest difference over the colour channels.</summary>
    public int MaxColour => Math.Max(MaxAbsolute[0], Math.Max(MaxAbsolute[1], MaxAbsolute[2]));
}
