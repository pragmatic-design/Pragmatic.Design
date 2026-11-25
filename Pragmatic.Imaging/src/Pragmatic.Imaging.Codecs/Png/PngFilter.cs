using System.Numerics;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     The five scanline filters of PNG, both ways: <see cref="Unfilter" /> reconstructs a row from the
///     filtered bytes and the row above; <see cref="Filter" /> does the reverse for the encoder.
///     The <c>step</c> argument is the byte distance to the pixel on the left. Filtering reads only the
///     raw row and the one above, so every filter runs a vector at a time; unfiltering Sub, Average and
///     Paeth depends on the byte just reconstructed, so those run per byte.
/// </summary>
internal static class PngFilter
{
    public const byte None = 0;
    public const byte Sub = 1;
    public const byte Up = 2;
    public const byte Average = 3;
    public const byte Paeth = 4;

    /// <summary>Turns <paramref name="row" /> from filtered to raw, in place. <paramref name="prior" /> is the raw row above, zeros for the first.</summary>
    public static void Unfilter(byte type, Span<byte> row, ReadOnlySpan<byte> prior, int step)
    {
        var vectorPixels = PngUnfilterSimd.IsSupported && step is 3 or 4 && row.Length % step == 0;
        switch (type)
        {
            case None:
                return;
            case Sub:
                if (vectorPixels)
                {
                    PngUnfilterSimd.Sub(row, step);
                    return;
                }

                for (var i = step; i < row.Length; i++)
                    row[i] += row[i - step];
                return;
            case Up:
                AddRows(row, prior);
                return;
            case Average:
                if (vectorPixels)
                {
                    PngUnfilterSimd.Average(row, prior, step);
                    return;
                }

                for (var i = 0; i < step; i++)
                    row[i] += (byte)(prior[i] >> 1);
                for (var i = step; i < row.Length; i++)
                    row[i] += (byte)((row[i - step] + prior[i]) >> 1);
                return;
            case Paeth:
                if (vectorPixels)
                {
                    PngUnfilterSimd.Paeth(row, prior, step);
                    return;
                }

                for (var i = 0; i < step; i++)
                    row[i] += prior[i];
                for (var i = step; i < row.Length; i++)
                    row[i] += PaethPredictor(row[i - step], prior[i], prior[i - step]);
                return;
            default:
                throw new ImageDecodeException($"PNG scanline filter {type} does not exist.");
        }
    }

    /// <summary>Writes the filtered form of <paramref name="raw" /> into <paramref name="output" />.</summary>
    public static void Filter(byte type, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> prior, int step, Span<byte> output)
    {
        switch (type)
        {
            case None:
                raw.CopyTo(output);
                return;
            case Sub:
                raw[..step].CopyTo(output);
                SubtractSpans(raw[step..], raw[..^step], output[step..]);
                return;
            case Up:
                SubtractSpans(raw, prior, output);
                return;
            case Average:
                for (var i = 0; i < step; i++)
                    output[i] = (byte)(raw[i] - (prior[i] >> 1));
                FilterAverage(raw[step..], raw[..^step], prior[step..], output[step..]);
                return;
            case Paeth:
                for (var i = 0; i < step; i++)
                    output[i] = (byte)(raw[i] - prior[i]);
                FilterPaeth(raw[step..], raw[..^step], prior[step..], prior[..^step], output[step..]);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "PNG scanline filter does not exist.");
        }
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    /// <summary>row[i] += prior[i], wrapping, a vector at a time.</summary>
    private static void AddRows(Span<byte> row, ReadOnlySpan<byte> prior)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && row.Length >= Vector<byte>.Count)
        {
            var rows = MemoryMarshal.Cast<byte, Vector<byte>>(row);
            var priors = MemoryMarshal.Cast<byte, Vector<byte>>(prior);
            for (var v = 0; v < rows.Length; v++)
                rows[v] += priors[v];
            i = rows.Length * Vector<byte>.Count;
        }

        for (; i < row.Length; i++)
            row[i] += prior[i];
    }

    /// <summary>output[i] = a[i] − b[i], wrapping.</summary>
    private static void SubtractSpans(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> output)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<byte>.Count)
        {
            var va = MemoryMarshal.Cast<byte, Vector<byte>>(a);
            var vb = MemoryMarshal.Cast<byte, Vector<byte>>(b);
            var vo = MemoryMarshal.Cast<byte, Vector<byte>>(output);
            for (var v = 0; v < va.Length; v++)
                vo[v] = va[v] - vb[v];
            i = va.Length * Vector<byte>.Count;
        }

        for (; i < a.Length; i++)
            output[i] = (byte)(a[i] - b[i]);
    }

    /// <summary>output[i] = raw[i] − ⌊(left[i] + up[i]) / 2⌋; the mean of two bytes without widening is (x &amp; y) + ((x ^ y) &gt;&gt; 1).</summary>
    private static void FilterAverage(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> left, ReadOnlySpan<byte> up, Span<byte> output)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && raw.Length >= Vector<byte>.Count)
        {
            var vr = MemoryMarshal.Cast<byte, Vector<byte>>(raw);
            var vl = MemoryMarshal.Cast<byte, Vector<byte>>(left);
            var vu = MemoryMarshal.Cast<byte, Vector<byte>>(up);
            var vo = MemoryMarshal.Cast<byte, Vector<byte>>(output);
            for (var v = 0; v < vr.Length; v++)
            {
                var l = vl[v];
                var u = vu[v];
                var mean = (l & u) + Vector.ShiftRightLogical(l ^ u, 1);
                vo[v] = vr[v] - mean;
            }

            i = vr.Length * Vector<byte>.Count;
        }

        for (; i < raw.Length; i++)
            output[i] = (byte)(raw[i] - ((left[i] + up[i]) >> 1));
    }

    /// <summary>
    ///     output[i] = raw[i] − Paeth(left, up, upLeft): the predictor is the neighbour closest to
    ///     left + up − upLeft, computed in 16-bit lanes where the differences fit.
    /// </summary>
    private static void FilterPaeth(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> left, ReadOnlySpan<byte> up, ReadOnlySpan<byte> upLeft, Span<byte> output)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && raw.Length >= Vector<byte>.Count)
        {
            var vr = MemoryMarshal.Cast<byte, Vector<byte>>(raw);
            var va = MemoryMarshal.Cast<byte, Vector<byte>>(left);
            var vb = MemoryMarshal.Cast<byte, Vector<byte>>(up);
            var vc = MemoryMarshal.Cast<byte, Vector<byte>>(upLeft);
            var vo = MemoryMarshal.Cast<byte, Vector<byte>>(output);
            for (var v = 0; v < vr.Length; v++)
            {
                Vector.Widen(va[v], out var aLow, out var aHigh);
                Vector.Widen(vb[v], out var bLow, out var bHigh);
                Vector.Widen(vc[v], out var cLow, out var cHigh);
                var predictedLow = PaethLanes(Vector.AsVectorInt16(aLow), Vector.AsVectorInt16(bLow), Vector.AsVectorInt16(cLow));
                var predictedHigh = PaethLanes(Vector.AsVectorInt16(aHigh), Vector.AsVectorInt16(bHigh), Vector.AsVectorInt16(cHigh));
                var predicted = Vector.Narrow(Vector.AsVectorUInt16(predictedLow), Vector.AsVectorUInt16(predictedHigh));
                vo[v] = vr[v] - predicted;
            }

            i = vr.Length * Vector<byte>.Count;
        }

        for (; i < raw.Length; i++)
            output[i] = (byte)(raw[i] - PaethPredictor(left[i], up[i], upLeft[i]));
    }

    /// <summary>The Paeth predictor on 16-bit lanes: pa = |b − c|, pb = |a − c|, pc = |a + b − 2c|.</summary>
    private static Vector<short> PaethLanes(Vector<short> a, Vector<short> b, Vector<short> c)
    {
        var pa = Vector.Abs(b - c);
        var pb = Vector.Abs(a - c);
        var pc = Vector.Abs(a + b - c - c);
        var useA = Vector.LessThanOrEqual(pa, pb) & Vector.LessThanOrEqual(pa, pc);
        var useB = Vector.LessThanOrEqual(pb, pc);
        return Vector.ConditionalSelect(useA, a, Vector.ConditionalSelect(useB, b, c));
    }
}
