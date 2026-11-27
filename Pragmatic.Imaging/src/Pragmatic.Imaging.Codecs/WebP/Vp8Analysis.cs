// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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

    // Work buffers laid out as Vp8Borders lays them: each prediction with its neighbours, and the source.
    private readonly byte[] _y = new byte[S * Vp8Borders.LumaRows];
    private readonly byte[] _sourceY = new byte[S * Vp8Borders.LumaRows];
    private readonly byte[] _u = new byte[S * Vp8Borders.ChromaRows];
    private readonly byte[] _sourceU = new byte[S * Vp8Borders.ChromaRows];
    private readonly byte[] _v = new byte[S * Vp8Borders.ChromaRows];
    private readonly byte[] _sourceV = new byte[S * Vp8Borders.ChromaRows];
    private readonly int[] _alphas = new int[MaxAlpha + 1];
    private byte[]? _segmentOfMacroblock;

    /// <summary>The segment of each macroblock, raster order, in an array from the pool that may be longer.</summary>
    public byte[] SegmentOfMacroblock => _segmentOfMacroblock ?? throw new InvalidOperationException("No picture has been analysed.");

    /// <summary>How many macroblocks there are.</summary>
    public int Count { get; private set; }

    /// <summary>Each segment's susceptibility against the mean, -127 to 127: the higher, the coarser it may be quantized.</summary>
    public int[] Alpha { get; } = new int[Segments];

    /// <summary>Each segment's susceptibility against the least, 0 to 255: the lower, the less it is filtered.</summary>
    public int[] Beta { get; } = new int[Segments];

    /// <summary>The mean susceptibility of the chroma, around 60: how coarsely chroma may be quantized.</summary>
    public int UvAlpha { get; private set; }

    /// <summary>Gives the segment map back to the pool; the analysis is reused for the next picture.</summary>
    public void Release()
    {
        if (_segmentOfMacroblock is null)
            return;
        ArrayPool<byte>.Shared.Return(_segmentOfMacroblock);
        _segmentOfMacroblock = null;
    }

    /// <summary>
    ///     Analyses <paramref name="rgba" />, in place of whatever was analysed before, converting it a macroblock
    ///     row at a time into the bands of <paramref name="picture" />.
    /// </summary>
    public void Run(Vp8Picture picture, ReadOnlySpan<byte> rgba)
    {
        Release();
        var count = picture.MacroblocksWide * picture.MacroblocksHigh;
        var mbAlpha = ArrayPool<byte>.Shared.Rent(count);
        _segmentOfMacroblock = mbAlpha;
        Count = count;
        var alphas = _alphas.AsSpan();
        alphas.Clear();
        byte[] y = _y, source = _sourceY, u = _u, sourceU = _sourceU, v = _v, sourceV = _sourceV;
        long uvSum = 0;
        for (var mbY = 0; mbY < picture.MacroblocksHigh; mbY++)
        {
            picture.ConvertRow(rgba, mbY);
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

        AssignSegments(alphas, mbAlpha.AsSpan(0, count), Alpha, Beta);
        UvAlpha = (int)(uvSum / count);
    }

    /// <summary>
    ///     Copies the macroblock's source into <paramref name="source" /> and its neighbours, from the source
    ///     too, around it in <paramref name="context" />: the analysis pretends the reconstruction is perfect.
    ///     <paramref name="plane" /> is the band of the macroblock's row, as <see cref="Vp8Picture" /> holds it.
    /// </summary>
    private static void Load(byte[] plane, int stride, int mbX, int mbY, int size, byte[] context, byte[] source)
    {
        var x0 = mbX * size;
        const int y0 = Vp8Picture.FirstRow;
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
        Span<int> histogram = stackalloc int[4 * (MaxCoefficientBin + 1)];
        Span<short> coefficients = stackalloc short[32];
        for (var by = 0; by < size; by += 4)
        {
            for (var bx = 0; bx < size; bx += 8)
                Collect(source, prediction, Vp8Borders.Origin + by * S + bx, coefficients, histogram);
        }

        return HistogramAlpha(histogram);
    }

    private static int ChromaSusceptibility(byte[] sourceU, byte[] u, byte[] sourceV, byte[] v)
    {
        Span<int> histogram = stackalloc int[4 * (MaxCoefficientBin + 1)];
        Span<short> coefficients = stackalloc short[32];
        for (var by = 0; by < 8; by += 4)
        {
            Collect(sourceU, u, Vp8Borders.Origin + by * S, coefficients, histogram);
            Collect(sourceV, v, Vp8Borders.Origin + by * S, coefficients, histogram);
        }

        return HistogramAlpha(histogram);
    }

    /// <summary>Counts the coefficients of the two side-by-side blocks at <paramref name="offset" /> into the histograms.</summary>
    private static void Collect(byte[] source, byte[] prediction, int offset, Span<short> coefficients, Span<int> histogram)
    {
        Vp8ForwardTransform.Dct2(source, prediction, offset, coefficients);
        if (coefficients.Length < 32 || histogram.Length < 4 * (MaxCoefficientBin + 1))
            throw new ArgumentException("Two blocks have 32 coefficients and the histograms 32 bins each.");

        // Each coefficient's bin, |c| / 8 up to the last, sixteen at once; then the counts.
        ref var values = ref MemoryMarshal.GetReference(coefficients);
        var last = Vector128.Create((short)MaxCoefficientBin);
        ref var counts = ref MemoryMarshal.GetReference(histogram);
        const int b = MaxCoefficientBin + 1;
        for (var block = 0; block < 32; block += 16)
        {
            var low = Vector128.Min(Vector128.ShiftRightArithmetic(Vector128.Abs(Vector128.LoadUnsafe(ref values, (nuint)block)), 3), last);
            var high = Vector128.Min(Vector128.ShiftRightArithmetic(Vector128.Abs(Vector128.LoadUnsafe(ref values, (nuint)(block + 8))), 3), last);

            // Four histograms side by side, a lane in each in turn, so no increment waits on the one before.
            for (var i = 0; i < 8; i += 4)
            {
                Unsafe.Add(ref counts, low.GetElement(i))++;
                Unsafe.Add(ref counts, b + low.GetElement(i + 1))++;
                Unsafe.Add(ref counts, 2 * b + low.GetElement(i + 2))++;
                Unsafe.Add(ref counts, 3 * b + low.GetElement(i + 3))++;
                Unsafe.Add(ref counts, high.GetElement(i))++;
                Unsafe.Add(ref counts, b + high.GetElement(i + 1))++;
                Unsafe.Add(ref counts, 2 * b + high.GetElement(i + 2))++;
                Unsafe.Add(ref counts, 3 * b + high.GetElement(i + 3))++;
            }
        }
    }

    /// <summary>How far out the histogram reaches against how tall its peak is (libwebp's <c>GetAlpha</c>).</summary>
    internal static int HistogramAlpha(ReadOnlySpan<int> histogram)
    {
        const int b = MaxCoefficientBin + 1;
        if (histogram.Length < 4 * b || b % Vector128<int>.Count != 0)
            throw new ArgumentException("The histograms have 32 bins each.");

        // The four histograms summed bin by bin, a vector of bins at a time; the tallest bin and the last one
        // that is not empty from the sums.
        ref var counts = ref MemoryMarshal.GetReference(histogram);
        var max = Vector128<int>.Zero;
        var nonEmpty = 0u;
        for (var k = 0; k < b; k += Vector128<int>.Count)
        {
            var sum = Vector128.LoadUnsafe(ref counts, (nuint)k) + Vector128.LoadUnsafe(ref counts, (nuint)(b + k)) +
                      Vector128.LoadUnsafe(ref counts, (nuint)(2 * b + k)) + Vector128.LoadUnsafe(ref counts, (nuint)(3 * b + k));
            max = Vector128.Max(max, sum);
            nonEmpty |= Vector128.GreaterThan(sum, Vector128<int>.Zero).ExtractMostSignificantBits() << k;
        }

        var tallest = Math.Max(Math.Max(max.GetElement(0), max.GetElement(1)), Math.Max(max.GetElement(2), max.GetElement(3)));
        var lastNonZero = nonEmpty == 0 ? 1 : 31 - BitOperations.LeadingZeroCount(nonEmpty);
        return tallest > 1 ? 2 * MaxAlpha * lastNonZero / tallest : 0;
    }

    /// <summary>A k-means of the macroblocks' values into the segments (libwebp's <c>AssignSegments</c>).</summary>
    private static void AssignSegments(ReadOnlySpan<int> alphas, Span<byte> mbAlpha, Span<int> alpha, Span<int> beta)
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

        // Each macroblock's value becomes its segment, in place.
        for (var i = 0; i < mbAlpha.Length; i++)
            mbAlpha[i] = (byte)map[mbAlpha[i]];

        int lowest = centers[0], highest = centers[0];
        foreach (var c in centers)
        {
            lowest = Math.Min(lowest, c);
            highest = Math.Max(highest, c);
        }

        if (highest == lowest)
            highest = lowest + 1;
        for (var n = 0; n < Segments; n++)
        {
            alpha[n] = Math.Clamp(255 * (centers[n] - average) / (highest - lowest), -127, 127);
            beta[n] = Math.Clamp(255 * (centers[n] - lowest) / (highest - lowest), 0, 255);
        }
    }
}
