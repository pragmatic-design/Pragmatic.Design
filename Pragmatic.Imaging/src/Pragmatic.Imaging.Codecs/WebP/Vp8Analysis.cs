namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Sorts the macroblocks into four segments by how hard each is to compress, as libwebp's analysis does:
///     a macroblock's "susceptibility" comes from the histogram of its DCT coefficients against its two
///     simplest predictions from the source, the segments are a k-means of those values, and each segment's
///     distance from the mean decides how much finer or coarser its quantizer is than the frame's.
/// </summary>
internal sealed class Vp8Analysis
{
    public const int Segments = 4;
    private const int MaxAlpha = 255;
    private const int MaxCoefficientBin = 31;
    private const int S = Vp8Prediction.Stride;

    private Vp8Analysis(byte[] segmentOfMacroblock, int[] alpha, int[] beta, int uvAlpha)
    {
        SegmentOfMacroblock = segmentOfMacroblock;
        Alpha = alpha;
        Beta = beta;
        UvAlpha = uvAlpha;
    }

    /// <summary>The segment of each macroblock, raster order.</summary>
    public byte[] SegmentOfMacroblock { get; }

    /// <summary>Each segment's susceptibility against the mean, -127 to 127: the higher, the coarser it may be quantized.</summary>
    public int[] Alpha { get; }

    /// <summary>Each segment's susceptibility against the least, 0 to 255: the lower, the less it is filtered.</summary>
    public int[] Beta { get; }

    /// <summary>The mean susceptibility of the chroma, around 60: how coarsely chroma may be quantized.</summary>
    public int UvAlpha { get; }

    public static Vp8Analysis Run(Vp8Picture picture)
    {
        var count = picture.MacroblocksWide * picture.MacroblocksHigh;
        var alphas = new int[MaxAlpha + 1];
        var mbAlpha = new byte[count];
        long uvSum = 0;
        var y = new byte[S * Vp8Borders.LumaRows];
        var u = new byte[S * Vp8Borders.ChromaRows];
        var v = new byte[S * Vp8Borders.ChromaRows];
        var source = new byte[S * Vp8Borders.LumaRows];
        var sourceU = new byte[S * Vp8Borders.ChromaRows];
        var sourceV = new byte[S * Vp8Borders.ChromaRows];
        for (var mbY = 0; mbY < picture.MacroblocksHigh; mbY++)
        {
            for (var mbX = 0; mbX < picture.MacroblocksWide; mbX++)
            {
                Load(picture.Y, picture.YStride, mbX, mbY, 16, y, source);
                Load(picture.U, picture.UvStride, mbX, mbY, 8, u, sourceU);
                Load(picture.V, picture.UvStride, mbX, mbY, 8, v, sourceV);

                // The two simplest predictions, DC and true motion, as libwebp's analysis tries.
                var lumaAlpha = -1;
                for (var m = 0; m < 2; m++)
                {
                    var mode = m == 0 ? Vp8Modes.Dc : Vp8Modes.Tm;
                    Vp8Prediction.Block(y, Vp8Borders.Origin, 16, mode, mbY > 0, mbX > 0);
                    lumaAlpha = Math.Max(lumaAlpha, Susceptibility(source, y, 16));
                }

                var chromaAlpha = -1;
                for (var m = 0; m < 2; m++)
                {
                    var mode = m == 0 ? Vp8Modes.Dc : Vp8Modes.Tm;
                    Vp8Prediction.Block(u, Vp8Borders.Origin, 8, mode, mbY > 0, mbX > 0);
                    Vp8Prediction.Block(v, Vp8Borders.Origin, 8, mode, mbY > 0, mbX > 0);
                    chromaAlpha = Math.Max(chromaAlpha, ChromaSusceptibility(sourceU, u, sourceV, v));
                }

                var mixed = (3 * lumaAlpha + chromaAlpha + 2) >> 2;
                var final = Math.Clamp(MaxAlpha - mixed, 0, MaxAlpha);
                alphas[final]++;
                mbAlpha[mbY * picture.MacroblocksWide + mbX] = (byte)final;
                uvSum += chromaAlpha;
            }
        }

        // Each macroblock's value becomes its segment, in place.
        var (alpha, beta) = AssignSegments(alphas, mbAlpha, mbAlpha);
        return new Vp8Analysis(mbAlpha, alpha, beta, (int)(uvSum / count));
    }

    /// <summary>
    ///     Copies the macroblock's source into <paramref name="source" /> and its neighbours, from the source
    ///     too, around it in <paramref name="context" />: the analysis pretends the reconstruction is perfect.
    /// </summary>
    private static void Load(byte[] plane, int stride, int mbX, int mbY, int size, byte[] context, byte[] source)
    {
        var x0 = mbX * size;
        var y0 = mbY * size;
        for (var j = 0; j < size; j++)
            plane.AsSpan((y0 + j) * stride + x0, size).CopyTo(source.AsSpan(Vp8Borders.Origin + j * S, size));

        for (var j = 0; j < size; j++)
            context[Vp8Borders.Origin + j * S - 1] = mbX > 0 ? plane[(y0 + j) * stride + x0 - 1] : (byte)129;
        if (mbY > 0)
            plane.AsSpan((y0 - 1) * stride + x0, size).CopyTo(context.AsSpan(Vp8Borders.Origin - S, size));
        else
            context.AsSpan(Vp8Borders.Origin - S, size).Fill(127);
        context[Vp8Borders.Origin - S - 1] = mbY == 0 ? (byte)127 : mbX == 0 ? (byte)129 : plane[(y0 - 1) * stride + x0 - 1];
    }

    private static int Susceptibility(byte[] source, byte[] prediction, int size)
    {
        Span<int> histogram = stackalloc int[MaxCoefficientBin + 1];
        Span<short> coefficients = stackalloc short[16];
        for (var by = 0; by < size; by += 4)
        {
            for (var bx = 0; bx < size; bx += 4)
                Collect(source, prediction, Vp8Borders.Origin + by * S + bx, coefficients, histogram);
        }

        return HistogramAlpha(histogram);
    }

    private static int ChromaSusceptibility(byte[] sourceU, byte[] u, byte[] sourceV, byte[] v)
    {
        Span<int> histogram = stackalloc int[MaxCoefficientBin + 1];
        Span<short> coefficients = stackalloc short[16];
        for (var by = 0; by < 8; by += 4)
        {
            for (var bx = 0; bx < 8; bx += 4)
            {
                Collect(sourceU, u, Vp8Borders.Origin + by * S + bx, coefficients, histogram);
                Collect(sourceV, v, Vp8Borders.Origin + by * S + bx, coefficients, histogram);
            }
        }

        return HistogramAlpha(histogram);
    }

    private static void Collect(byte[] source, byte[] prediction, int offset, Span<short> coefficients, Span<int> histogram)
    {
        Vp8ForwardTransform.Dct(source, prediction, offset, coefficients);
        foreach (var c in coefficients)
            histogram[Math.Min(Math.Abs((int)c) >> 3, MaxCoefficientBin)]++;
    }

    /// <summary>How far out the histogram reaches against how tall its peak is (libwebp's <c>GetAlpha</c>).</summary>
    private static int HistogramAlpha(ReadOnlySpan<int> histogram)
    {
        int max = 0, lastNonZero = 1;
        for (var k = 0; k <= MaxCoefficientBin; k++)
        {
            if (histogram[k] <= 0)
                continue;
            max = Math.Max(max, histogram[k]);
            lastNonZero = k;
        }

        return max > 1 ? 2 * MaxAlpha * lastNonZero / max : 0;
    }

    /// <summary>A k-means of the macroblocks' values into the segments (libwebp's <c>AssignSegments</c>).</summary>
    private static (int[] Alpha, int[] Beta) AssignSegments(int[] alphas, byte[] mbAlpha, byte[] segments)
    {
        int min = 0, max;
        while (min <= MaxAlpha && alphas[min] == 0)
            min++;
        for (max = MaxAlpha; max > min && alphas[max] == 0; max--)
        {
        }

        var range = max - min;
        Span<int> centers = stackalloc int[Segments];
        for (int k = 0, n = 1; k < Segments; k++, n += 2)
            centers[k] = min + n * range / (2 * Segments);

        Span<int> map = stackalloc int[MaxAlpha + 1];
        Span<int> accumulated = stackalloc int[Segments];
        Span<int> weighted = stackalloc int[Segments];
        var average = 0;
        for (var iteration = 0; iteration < 6; iteration++)
        {
            accumulated.Clear();
            weighted.Clear();
            var nearest = 0;
            for (var a = min; a <= max; a++)
            {
                if (alphas[a] == 0)
                    continue;
                while (nearest + 1 < Segments && Math.Abs(a - centers[nearest + 1]) < Math.Abs(a - centers[nearest]))
                    nearest++;
                map[a] = nearest;
                weighted[nearest] += a * alphas[a];
                accumulated[nearest] += alphas[a];
            }

            var displaced = 0;
            average = 0;
            var total = 0;
            for (var n = 0; n < Segments; n++)
            {
                if (accumulated[n] == 0)
                    continue;
                var center = (weighted[n] + accumulated[n] / 2) / accumulated[n];
                displaced += Math.Abs(centers[n] - center);
                centers[n] = center;
                average += center * accumulated[n];
                total += accumulated[n];
            }

            average = (average + total / 2) / total;
            if (displaced < 5)
                break;
        }

        for (var i = 0; i < mbAlpha.Length; i++)
            segments[i] = (byte)map[mbAlpha[i]];

        int lowest = centers[0], highest = centers[0];
        foreach (var c in centers)
        {
            lowest = Math.Min(lowest, c);
            highest = Math.Max(highest, c);
        }

        if (highest == lowest)
            highest = lowest + 1;
        var alpha = new int[Segments];
        var beta = new int[Segments];
        for (var n = 0; n < Segments; n++)
        {
            alpha[n] = Math.Clamp(255 * (centers[n] - average) / (highest - lowest), -127, 127);
            beta[n] = Math.Clamp(255 * (centers[n] - lowest) / (highest - lowest), 0, 255);
        }

        return (alpha, beta);
    }
}
