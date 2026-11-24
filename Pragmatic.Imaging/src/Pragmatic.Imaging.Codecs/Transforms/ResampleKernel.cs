namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>A filter as a function of distance from the sample, and how far from it that function is non-zero.</summary>
internal readonly record struct ResampleKernel(double Support, Func<double, double> Weight)
{
    public static ResampleKernel For(ResizeFilter filter) => filter switch
    {
        ResizeFilter.Triangle => new ResampleKernel(1.0, Triangle),
        ResizeFilter.CatmullRom => new ResampleKernel(2.0, CatmullRom),
        ResizeFilter.Gaussian => new ResampleKernel(3.0, Gaussian),
        ResizeFilter.Lanczos3 => new ResampleKernel(3.0, Lanczos3),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Not a kernel filter."),
    };

    private static double Triangle(double x)
    {
        x = Math.Abs(x);
        return x < 1 ? 1 - x : 0;
    }

    /// <summary>The Mitchell-Netravali cubic with B = 0 and C = 1/2.</summary>
    private static double CatmullRom(double x)
    {
        x = Math.Abs(x);
        if (x < 1)
            return (1.5 * x - 2.5) * x * x + 1;
        if (x < 2)
            return ((-0.5 * x + 2.5) * x - 4) * x + 2;
        return 0;
    }

    /// <summary>Sigma 1, cut at radius 3.</summary>
    private static double Gaussian(double x)
    {
        if (Math.Abs(x) >= 3)
            return 0;
        return Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);
    }

    private static double Lanczos3(double x)
    {
        if (x == 0)
            return 1;
        x = Math.Abs(x);
        if (x >= 3)
            return 0;
        var px = Math.PI * x;
        return 3 * Math.Sin(px) * Math.Sin(px / 3) / (px * px);
    }
}
