using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Quantization;

/// <summary>
///     Xiaolin Wu's colour quantizer ("Efficient Statistical Computations for Optimal Color Quantization",
///     Graphics Gems II, 1991): the colours, at 5 bits a channel, are counted into a cube; the cube is split, box
///     by box, where a cut along one axis reduces the total variance most, until there are as many boxes as the
///     palette has entries; each entry is the mean of its box. Deterministic: the same pixels give the same
///     palette.
/// </summary>
public static class WuQuantizer
{
    private const int Side = 33;
    private const int Cells = Side * Side * Side;

    /// <summary>
    ///     A palette of at most <paramref name="colours" /> entries (R, G, B) for the pixels of
    ///     <paramref name="rgba" /> whose alpha is not zero. Returns the number of entries written to
    ///     <paramref name="palette" />, fewer when the pixels have fewer distinct 5-bit colours.
    /// </summary>
    public static int Quantize(ReadOnlySpan<byte> rgba, int colours, Span<byte> palette)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(colours, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(colours, 256);
        if (palette.Length < colours * 3)
            throw new ArgumentException("The palette has room for fewer entries than asked for.", nameof(palette));

        var weights = ArrayPool<long>.Shared.Rent(Cells);
        var reds = ArrayPool<long>.Shared.Rent(Cells);
        var greens = ArrayPool<long>.Shared.Rent(Cells);
        var blues = ArrayPool<long>.Shared.Rent(Cells);
        var squares = ArrayPool<double>.Shared.Rent(Cells);
        try
        {
            var moments = new Moments(weights.AsSpan(0, Cells), reds.AsSpan(0, Cells), greens.AsSpan(0, Cells), blues.AsSpan(0, Cells),
                squares.AsSpan(0, Cells));
            moments.Clear();
            Histogram(rgba, moments);
            moments.Accumulate();
            return Partition(moments, colours, palette);
        }
        finally
        {
            ArrayPool<long>.Shared.Return(weights);
            ArrayPool<long>.Shared.Return(reds);
            ArrayPool<long>.Shared.Return(greens);
            ArrayPool<long>.Shared.Return(blues);
            ArrayPool<double>.Shared.Return(squares);
        }
    }

    private static int Index(int r, int g, int b) => (r * Side + g) * Side + b;

    private static void Histogram(ReadOnlySpan<byte> rgba, in Moments m)
    {
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
                continue;
            int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            var cell = Index((r >> 3) + 1, (g >> 3) + 1, (b >> 3) + 1);
            m.Weight[cell]++;
            m.Red[cell] += r;
            m.Green[cell] += g;
            m.Blue[cell] += b;
            m.Square[cell] += r * r + g * g + b * b;
        }
    }

    private static int Partition(in Moments m, int colours, Span<byte> palette)
    {
        Span<Box> boxes = stackalloc Box[256];
        Span<double> variance = stackalloc double[256];
        boxes[0] = new Box(0, Side - 1, 0, Side - 1, 0, Side - 1);
        var count = 1;
        var next = 0;
        while (count < colours)
        {
            ref var box = ref boxes[next];
            var made = Cut(m, ref box, ref boxes[count]);
            if (made)
            {
                variance[next] = box.Volume > 1 ? Variance(m, box) : 0;
                variance[count] = boxes[count].Volume > 1 ? Variance(m, boxes[count]) : 0;
                count++;
            }
            else
            {
                variance[next] = 0;
            }

            // The next box to cut is the one with the most variance left.
            next = 0;
            var largest = variance[0];
            for (var k = 1; k < count; k++)
            {
                if (variance[k] > largest)
                {
                    largest = variance[k];
                    next = k;
                }
            }

            if (largest <= 0)
                break;
        }

        var written = 0;
        for (var k = 0; k < count; k++)
        {
            var weight = Volume(boxes[k], m.Weight);
            if (weight == 0)
                continue;
            palette[written * 3] = (byte)((Volume(boxes[k], m.Red) + weight / 2) / weight);
            palette[written * 3 + 1] = (byte)((Volume(boxes[k], m.Green) + weight / 2) / weight);
            palette[written * 3 + 2] = (byte)((Volume(boxes[k], m.Blue) + weight / 2) / weight);
            written++;
        }

        return written;
    }

    private static bool Cut(in Moments m, ref Box box, ref Box other)
    {
        var wholeR = Volume(box, m.Red);
        var wholeG = Volume(box, m.Green);
        var wholeB = Volume(box, m.Blue);
        var wholeW = Volume(box, m.Weight);
        var (maxR, cutR) = Maximize(m, box, Axis.Red, box.R0 + 1, box.R1, wholeR, wholeG, wholeB, wholeW);
        var (maxG, cutG) = Maximize(m, box, Axis.Green, box.G0 + 1, box.G1, wholeR, wholeG, wholeB, wholeW);
        var (maxB, cutB) = Maximize(m, box, Axis.Blue, box.B0 + 1, box.B1, wholeR, wholeG, wholeB, wholeW);

        Axis axis;
        if (maxR >= maxG && maxR >= maxB)
        {
            if (cutR < 0)
                return false;
            axis = Axis.Red;
        }
        else
        {
            axis = maxG >= maxB ? Axis.Green : Axis.Blue;
        }

        other = box;
        switch (axis)
        {
            case Axis.Red:
                other.R0 = box.R1 = cutR;
                break;
            case Axis.Green:
                other.G0 = box.G1 = cutG;
                break;
            default:
                other.B0 = box.B1 = cutB;
                break;
        }

        return true;
    }

    private static (double Max, int Cut) Maximize(in Moments m, in Box box, Axis axis, int first, int last, long wholeR, long wholeG,
        long wholeB, long wholeW)
    {
        var baseR = Bottom(box, axis, m.Red);
        var baseG = Bottom(box, axis, m.Green);
        var baseB = Bottom(box, axis, m.Blue);
        var baseW = Bottom(box, axis, m.Weight);
        var max = 0.0;
        var cut = -1;
        for (var i = first; i < last; i++)
        {
            var halfR = baseR + Top(box, axis, i, m.Red);
            var halfG = baseG + Top(box, axis, i, m.Green);
            var halfB = baseB + Top(box, axis, i, m.Blue);
            var halfW = baseW + Top(box, axis, i, m.Weight);
            if (halfW == 0)
                continue;
            var restW = wholeW - halfW;
            if (restW == 0)
                continue;
            var temp = ((double)halfR * halfR + (double)halfG * halfG + (double)halfB * halfB) / halfW;
            var restR = wholeR - halfR;
            var restG = wholeG - halfG;
            var restB = wholeB - halfB;
            temp += ((double)restR * restR + (double)restG * restG + (double)restB * restB) / restW;
            if (temp > max)
            {
                max = temp;
                cut = i;
            }
        }

        return (max, cut);
    }

    private static double Variance(in Moments m, in Box box)
    {
        double r = Volume(box, m.Red), g = Volume(box, m.Green), b = Volume(box, m.Blue);
        var squares = VolumeOf(box, m.Square);
        double weight = Volume(box, m.Weight);
        return weight == 0 ? 0 : squares - (r * r + g * g + b * b) / weight;
    }

    // The sum of a moment over a box, from the cumulative cube by inclusion and exclusion.
    private static long Volume(in Box b, ReadOnlySpan<long> m) =>
        m[Index(b.R1, b.G1, b.B1)] - m[Index(b.R1, b.G1, b.B0)] - m[Index(b.R1, b.G0, b.B1)] + m[Index(b.R1, b.G0, b.B0)]
        - m[Index(b.R0, b.G1, b.B1)] + m[Index(b.R0, b.G1, b.B0)] + m[Index(b.R0, b.G0, b.B1)] - m[Index(b.R0, b.G0, b.B0)];

    private static double VolumeOf(in Box b, ReadOnlySpan<double> m) =>
        m[Index(b.R1, b.G1, b.B1)] - m[Index(b.R1, b.G1, b.B0)] - m[Index(b.R1, b.G0, b.B1)] + m[Index(b.R1, b.G0, b.B0)]
        - m[Index(b.R0, b.G1, b.B1)] + m[Index(b.R0, b.G1, b.B0)] + m[Index(b.R0, b.G0, b.B1)] - m[Index(b.R0, b.G0, b.B0)];

    // The part of a box's volume that does not depend on where along the axis it is cut.
    private static long Bottom(in Box b, Axis axis, ReadOnlySpan<long> m) => axis switch
    {
        Axis.Red => -m[Index(b.R0, b.G1, b.B1)] + m[Index(b.R0, b.G1, b.B0)] + m[Index(b.R0, b.G0, b.B1)] - m[Index(b.R0, b.G0, b.B0)],
        Axis.Green => -m[Index(b.R1, b.G0, b.B1)] + m[Index(b.R1, b.G0, b.B0)] + m[Index(b.R0, b.G0, b.B1)] - m[Index(b.R0, b.G0, b.B0)],
        _ => -m[Index(b.R1, b.G1, b.B0)] + m[Index(b.R1, b.G0, b.B0)] + m[Index(b.R0, b.G1, b.B0)] - m[Index(b.R0, b.G0, b.B0)],
    };

    // The part that does: the box's volume up to position i along the axis.
    private static long Top(in Box b, Axis axis, int i, ReadOnlySpan<long> m) => axis switch
    {
        Axis.Red => m[Index(i, b.G1, b.B1)] - m[Index(i, b.G1, b.B0)] - m[Index(i, b.G0, b.B1)] + m[Index(i, b.G0, b.B0)],
        Axis.Green => m[Index(b.R1, i, b.B1)] - m[Index(b.R1, i, b.B0)] - m[Index(b.R0, i, b.B1)] + m[Index(b.R0, i, b.B0)],
        _ => m[Index(b.R1, b.G1, i)] - m[Index(b.R1, b.G0, i)] - m[Index(b.R0, b.G1, i)] + m[Index(b.R0, b.G0, i)],
    };

    private enum Axis
    {
        Red,
        Green,
        Blue,
    }

    /// <summary>A box of the cube, exclusive of its lower corner and inclusive of its upper one.</summary>
    private struct Box(int r0, int r1, int g0, int g1, int b0, int b1)
    {
        public int R0 = r0, R1 = r1, G0 = g0, G1 = g1, B0 = b0, B1 = b1;

        public readonly int Volume => (R1 - R0) * (G1 - G0) * (B1 - B0);
    }

    /// <summary>The counts and sums of each 5-bit colour cell, made cumulative over the cube.</summary>
    private readonly ref struct Moments(Span<long> weight, Span<long> red, Span<long> green, Span<long> blue, Span<double> square)
    {
        public readonly Span<long> Weight = weight;
        public readonly Span<long> Red = red;
        public readonly Span<long> Green = green;
        public readonly Span<long> Blue = blue;
        public readonly Span<double> Square = square;

        public void Clear()
        {
            Weight.Clear();
            Red.Clear();
            Green.Clear();
            Blue.Clear();
            Square.Clear();
        }

        public void Accumulate()
        {
            Span<long> areaW = stackalloc long[Side];
            Span<long> areaR = stackalloc long[Side];
            Span<long> areaG = stackalloc long[Side];
            Span<long> areaB = stackalloc long[Side];
            Span<double> areaS = stackalloc double[Side];
            for (var r = 1; r < Side; r++)
            {
                areaW.Clear();
                areaR.Clear();
                areaG.Clear();
                areaB.Clear();
                areaS.Clear();
                for (var g = 1; g < Side; g++)
                {
                    long lineW = 0, lineR = 0, lineG = 0, lineB = 0;
                    double lineS = 0;
                    for (var b = 1; b < Side; b++)
                    {
                        var cell = Index(r, g, b);
                        lineW += Weight[cell];
                        lineR += Red[cell];
                        lineG += Green[cell];
                        lineB += Blue[cell];
                        lineS += Square[cell];
                        areaW[b] += lineW;
                        areaR[b] += lineR;
                        areaG[b] += lineG;
                        areaB[b] += lineB;
                        areaS[b] += lineS;
                        var below = Index(r - 1, g, b);
                        Weight[cell] = Weight[below] + areaW[b];
                        Red[cell] = Red[below] + areaR[b];
                        Green[cell] = Green[below] + areaG[b];
                        Blue[cell] = Blue[below] + areaB[b];
                        Square[cell] = Square[below] + areaS[b];
                    }
                }
            }
        }
    }
}
