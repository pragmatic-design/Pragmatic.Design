// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>Peak signal-to-noise ratio over the R, G and B samples together, in decibels, for 8-bit data.</summary>
public static class Psnr
{
    /// <returns><see cref="double.PositiveInfinity" /> for identical colour.</returns>
    public static double Rgb(RgbaImage actual, RgbaImage expected)
    {
        RgbaImage.RequireSameSize(actual, expected);
        var a = actual.Pixels;
        var e = expected.Pixels;
        long squares = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                long d = a[i + c] - e[i + c];
                squares += d * d;
            }
        }

        if (squares == 0)
            return double.PositiveInfinity;

        var mse = squares / (3.0 * actual.Width * actual.Height);
        return 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
