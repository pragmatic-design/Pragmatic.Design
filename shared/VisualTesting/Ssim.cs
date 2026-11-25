// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     Structural similarity (Wang, Bovik, Sheikh and Simoncelli, 2004): Gaussian window of 11 taps,
///     σ 1.5, K1 0.01, K2 0.03, population covariance, the mean over the map less a strip of the window's
///     radius along each edge.
/// </summary>
/// <remarks>
///     Written to compute what <c>skimage.metrics.structural_similarity(data_range=255,
///     gaussian_weights=True, sigma=1.5, use_sample_covariance=False)</c> computes, edges included: SciPy's
///     <c>reflect</c> mode, which repeats the edge sample (d c b a | a b c d). The test that holds it to
///     scikit-image is <c>TheMetricsMatchTheirIndependentImplementationsTests</c>.
/// </remarks>
public static class Ssim
{
    private const double Sigma = 1.5;
    private const int Radius = 5; // int(3.5 * σ + 0.5), as SciPy truncates the Gaussian
    private const double C1 = 0.01 * 255 * 0.01 * 255;
    private const double C2 = 0.03 * 255 * 0.03 * 255;

    private static readonly double[] Kernel = BuildKernel();

    /// <summary>SSIM of the Rec. 601 luma, 0.299 R + 0.587 G + 0.114 B, the weights JPEG uses.</summary>
    public static double Luma(RgbaImage actual, RgbaImage expected) => Mean(Map(LumaPlane(actual), LumaPlane(expected), actual.Width, actual.Height), actual.Width, actual.Height);

    /// <summary>SSIM of one channel: 0 R, 1 G, 2 B, 3 A.</summary>
    public static double Channel(RgbaImage actual, RgbaImage expected, int channel) =>
        Mean(Map(ChannelPlane(actual, channel), ChannelPlane(expected, channel), actual.Width, actual.Height), actual.Width, actual.Height);

    /// <summary>The SSIM of each pixel's neighbourhood, on luma: what the worst region of a failure is read from.</summary>
    public static double[] LumaMap(RgbaImage actual, RgbaImage expected) => Map(LumaPlane(actual), LumaPlane(expected), actual.Width, actual.Height);

    internal static double[] Map(double[] x, double[] y, int width, int height)
    {
        if (width <= 2 * Radius || height <= 2 * Radius)
            throw new ArgumentException($"SSIM needs an image larger than {2 * Radius + 1} pixels on each side, not {width} x {height}.");

        var xx = new double[x.Length];
        var yy = new double[x.Length];
        var xy = new double[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            xx[i] = x[i] * x[i];
            yy[i] = y[i] * y[i];
            xy[i] = x[i] * y[i];
        }

        var ux = Filter(x, width, height);
        var uy = Filter(y, width, height);
        var uxx = Filter(xx, width, height);
        var uyy = Filter(yy, width, height);
        var uxy = Filter(xy, width, height);

        var map = new double[x.Length];
        for (var i = 0; i < map.Length; i++)
        {
            var vx = uxx[i] - ux[i] * ux[i];
            var vy = uyy[i] - uy[i] * uy[i];
            var vxy = uxy[i] - ux[i] * uy[i];
            map[i] = (2 * ux[i] * uy[i] + C1) * (2 * vxy + C2) / ((ux[i] * ux[i] + uy[i] * uy[i] + C1) * (vx + vy + C2));
        }

        return map;
    }

    internal static double Mean(double[] map, int width, int height)
    {
        double sum = 0;
        for (var row = Radius; row < height - Radius; row++)
        {
            for (var column = Radius; column < width - Radius; column++)
                sum += map[row * width + column];
        }

        return sum / ((double)(width - 2 * Radius) * (height - 2 * Radius));
    }

    internal static double[] LumaPlane(RgbaImage image)
    {
        var plane = new double[image.Width * image.Height];
        var p = image.Pixels;
        for (int i = 0, o = 0; o < plane.Length; i += 4, o++)
            plane[o] = 0.299 * p[i] + 0.587 * p[i + 1] + 0.114 * p[i + 2];
        return plane;
    }

    private static double[] ChannelPlane(RgbaImage image, int channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 3);
        var plane = new double[image.Width * image.Height];
        var p = image.Pixels;
        for (int i = channel, o = 0; o < plane.Length; i += 4, o++)
            plane[o] = p[i];
        return plane;
    }

    /// <summary>The separable Gaussian, rows then columns, with SciPy's <c>reflect</c> edges.</summary>
    private static double[] Filter(double[] plane, int width, int height)
    {
        var rows = new double[plane.Length];
        for (var row = 0; row < height; row++)
        {
            var line = row * width;
            for (var column = 0; column < width; column++)
            {
                double sum = 0;
                for (var k = -Radius; k <= Radius; k++)
                    sum += Kernel[k + Radius] * plane[line + Reflect(column + k, width)];
                rows[line + column] = sum;
            }
        }

        var result = new double[plane.Length];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                double sum = 0;
                for (var k = -Radius; k <= Radius; k++)
                    sum += Kernel[k + Radius] * rows[Reflect(row + k, height) * width + column];
                result[row * width + column] = sum;
            }
        }

        return result;
    }

    private static int Reflect(int index, int length) => index < 0 ? -index - 1 : index >= length ? 2 * length - index - 1 : index;

    private static double[] BuildKernel()
    {
        var kernel = new double[2 * Radius + 1];
        double sum = 0;
        for (var k = -Radius; k <= Radius; k++)
        {
            kernel[k + Radius] = Math.Exp(-0.5 * k * k / (Sigma * Sigma));
            sum += kernel[k + Radius];
        }

        for (var k = 0; k < kernel.Length; k++)
            kernel[k] /= sum;
        return kernel;
    }
}
