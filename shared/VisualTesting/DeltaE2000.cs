// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     The CIEDE2000 colour difference (kL = kC = kH = 1), as Sharma, Wu and Dalal set it out in "The
///     CIEDE2000 Color-Difference Formula: Implementation Notes, Supplementary Test Data, and Mathematical
///     Observations" (2005), whose 34 test pairs hold it (<c>TheMetricsMatchTheirIndependentImplementationsTests</c>).
/// </summary>
public static class DeltaE2000
{
    private static readonly double Pow25To7 = Math.Pow(25, 7);

    public static double Between(CieLab first, CieLab second)
    {
        var c1 = Math.Sqrt(first.A * first.A + first.B * first.B);
        var c2 = Math.Sqrt(second.A * second.A + second.B * second.B);
        var cBar7 = Math.Pow((c1 + c2) / 2, 7);
        var g = 0.5 * (1 - Math.Sqrt(cBar7 / (cBar7 + Pow25To7)));
        var a1 = (1 + g) * first.A;
        var a2 = (1 + g) * second.A;
        var cp1 = Math.Sqrt(a1 * a1 + first.B * first.B);
        var cp2 = Math.Sqrt(a2 * a2 + second.B * second.B);
        var hp1 = Hue(first.B, a1);
        var hp2 = Hue(second.B, a2);

        var dL = second.L - first.L;
        var dC = cp2 - cp1;
        double dh;
        if (cp1 * cp2 == 0)
            dh = 0;
        else if (Math.Abs(hp2 - hp1) <= 180)
            dh = hp2 - hp1;
        else
            dh = hp2 - hp1 > 180 ? hp2 - hp1 - 360 : hp2 - hp1 + 360;
        var dH = 2 * Math.Sqrt(cp1 * cp2) * Math.Sin(Radians(dh / 2));

        var lBar = (first.L + second.L) / 2;
        var cpBar = (cp1 + cp2) / 2;
        double hBar;
        if (cp1 * cp2 == 0)
            hBar = hp1 + hp2;
        else if (Math.Abs(hp1 - hp2) <= 180)
            hBar = (hp1 + hp2) / 2;
        else
            hBar = hp1 + hp2 < 360 ? (hp1 + hp2 + 360) / 2 : (hp1 + hp2 - 360) / 2;

        var t = 1 - 0.17 * Math.Cos(Radians(hBar - 30)) + 0.24 * Math.Cos(Radians(2 * hBar))
                + 0.32 * Math.Cos(Radians(3 * hBar + 6)) - 0.20 * Math.Cos(Radians(4 * hBar - 63));
        var dTheta = 30 * Math.Exp(-Math.Pow((hBar - 275) / 25, 2));
        var cpBar7 = Math.Pow(cpBar, 7);
        var rc = 2 * Math.Sqrt(cpBar7 / (cpBar7 + Pow25To7));
        var lBar50 = (lBar - 50) * (lBar - 50);
        var sl = 1 + 0.015 * lBar50 / Math.Sqrt(20 + lBar50);
        var sc = 1 + 0.045 * cpBar;
        var sh = 1 + 0.015 * cpBar * t;
        var rt = -Math.Sin(Radians(2 * dTheta)) * rc;

        var l = dL / sl;
        var c = dC / sc;
        var h = dH / sh;
        return Math.Sqrt(l * l + c * c + h * h + rt * c * h);
    }

    /// <summary>Over two images of one size, the mean and the largest difference of corresponding pixels, alpha ignored.</summary>
    public static (double Mean, double Max) Images(RgbaImage actual, RgbaImage expected)
    {
        RgbaImage.RequireSameSize(actual, expected);
        var a = actual.Pixels;
        var e = expected.Pixels;
        double sum = 0;
        double max = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            var d = Between(CieLab.FromSrgb(a[i], a[i + 1], a[i + 2]), CieLab.FromSrgb(e[i], e[i + 1], e[i + 2]));
            sum += d;
            if (d > max)
                max = d;
        }

        return (sum / (a.Length / 4), max);
    }

    private static double Hue(double b, double aPrime)
    {
        if (b == 0 && aPrime == 0)
            return 0;
        var degrees = Math.Atan2(b, aPrime) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
