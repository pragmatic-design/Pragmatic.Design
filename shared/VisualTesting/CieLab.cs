// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>A colour in CIE L*a*b*, D65 white, 2° observer.</summary>
public readonly record struct CieLab(double L, double A, double B)
{
    private static readonly double[] Linear = BuildLinear();

    /// <summary>
    ///     8-bit sRGB to L*a*b*, with the constants of <c>skimage.color.rgb2lab</c>: the sRGB curve
    ///     (IEC 61966-2-1), its matrix to XYZ, D65 white (0.95047, 1, 1.08883), and the CIE 1976 cube root
    ///     with its linear segment below 0.008856.
    /// </summary>
    public static CieLab FromSrgb(byte red, byte green, byte blue)
    {
        var r = Linear[red];
        var g = Linear[green];
        var b = Linear[blue];
        var x = (0.412453 * r + 0.357580 * g + 0.180423 * b) / 0.95047;
        var y = 0.212671 * r + 0.715160 * g + 0.072169 * b;
        var z = (0.019334 * r + 0.119193 * g + 0.950227 * b) / 1.08883;
        var fx = F(x);
        var fy = F(y);
        var fz = F(z);
        return new CieLab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;

    private static double[] BuildLinear()
    {
        var table = new double[256];
        for (var i = 0; i < table.Length; i++)
        {
            var c = i / 255.0;
            table[i] = c > 0.04045 ? Math.Pow((c + 0.055) / 1.055, 2.4) : c / 12.92;
        }

        return table;
    }
}
