using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The inverse of the lossless predictor transform (RFC 9649, section 4.1): each pixel is the residue
///     plus the prediction of its block's mode. The mode is read once per block, and each block's run of a
///     row goes through a loop for its mode.
/// </summary>
internal static class Vp8LPredictor
{
    public static void Invert(Span<uint> pixels, int width, int height, int bits, uint[] modes)
    {
        // The top-left pixel is predicted by opaque black, the rest of the first row by its left
        // neighbour, and the first pixel of every other row by the pixel above it.
        pixels[0] = Vp8LInverseTransforms.Add(pixels[0], 0xFF000000);
        for (var x = 1; x < width; x++)
            pixels[x] = Vp8LInverseTransforms.Add(pixels[x], pixels[x - 1]);

        var blocksPerRow = Vp8LInverseTransforms.DivideRoundingUp(width, bits);
        var blockSize = 1 << bits;
        for (var y = 1; y < height; y++)
        {
            var row = y * width;
            pixels[row] = Vp8LInverseTransforms.Add(pixels[row], pixels[row - width]);
            var blockRow = (y >> bits) * blocksPerRow;
            for (var start = 0; start < width; start += blockSize)
            {
                var mode = (int)(modes[blockRow + (start >> bits)] >> 8) & 0xF;
                var from = Math.Max(start, 1);
                var to = Math.Min(start + blockSize, width);
                if (from < to)
                    Run(pixels, row + from, row + to, width, mode);
            }
        }
    }

    /// <summary>
    ///     Pixels <paramref name="from" /> to <paramref name="to" /> of one row, none in the first row or
    ///     column. The top-right pixel of the last column is, by memory position, the first pixel of the
    ///     current row: the format defines it that way.
    /// </summary>
    private static void Run(Span<uint> p, int from, int to, int width, int mode)
    {
        switch (mode)
        {
            case 1:
                for (var i = from; i < to; i++)
                    p[i] = Vp8LInverseTransforms.Add(p[i], p[i - 1]);
                return;
            case 2:
                AddRow(p, from, to, from - width);
                return;
            case 3:
                AddRow(p, from, to, from - width + 1);
                return;
            case 4:
                AddRow(p, from, to, from - width - 1);
                return;
            case 8:
                AverageRow(p, from, to, from - width - 1, from - width);
                return;
            case 9:
                AverageRow(p, from, to, from - width, from - width + 1);
                return;
            case 5:
                for (var i = from; i < to; i++)
                    p[i] = Vp8LInverseTransforms.Add(p[i], Average(Average(p[i - 1], p[i - width + 1]), p[i - width]));
                return;
            case 6:
                for (var i = from; i < to; i++)
                    p[i] = Vp8LInverseTransforms.Add(p[i], Average(p[i - 1], p[i - width - 1]));
                return;
            case 7:
                for (var i = from; i < to; i++)
                    p[i] = Vp8LInverseTransforms.Add(p[i], Average(p[i - 1], p[i - width]));
                return;
            case 10:
                for (var i = from; i < to; i++)
                    p[i] = Vp8LInverseTransforms.Add(p[i], Average(Average(p[i - 1], p[i - width - 1]), Average(p[i - width], p[i - width + 1])));
                return;
            case 11:
                SelectRun(p, from, to, width);
                return;
            case 12:
                for (var i = from; i < to; i++)
                    p[i] = Vp8LInverseTransforms.Add(p[i], ClampAddSubtractFull(p[i - 1], p[i - width], p[i - width - 1]));
                return;
            case 13:
                for (var i = from; i < to; i++)
                    p[i] = Vp8LInverseTransforms.Add(p[i], ClampAddSubtractHalf(Average(p[i - 1], p[i - width]), p[i - width - 1]));
                return;
            default:
                AddConstant(p.Slice(from, to - from), 0xFF000000);
                return;
        }
    }

    // The predictions that read only the row above, which is final, have no dependency between the
    // pixels of a run: they add a whole vector at a time. Per-channel addition modulo 256 is byte-wise
    // addition.

    private static void AddRow(Span<uint> p, int from, int to, int source)
    {
        var i = from;
        var count = Vector<uint>.Count;
        for (; i + count <= to; i += count)
        {
            var sum = Vector.AsVectorUInt32(Vector.AsVectorByte(new Vector<uint>(p[i..])) + Vector.AsVectorByte(new Vector<uint>(p[(source + i - from)..])));
            sum.CopyTo(p[i..]);
        }

        for (; i < to; i++)
            p[i] = Vp8LInverseTransforms.Add(p[i], p[source + i - from]);
    }

    private static void AverageRow(Span<uint> p, int from, int to, int first, int second)
    {
        var i = from;
        var count = Vector<uint>.Count;
        var low = new Vector<uint>(0xFEFEFEFE);
        for (; i + count <= to; i += count)
        {
            var a = new Vector<uint>(p[(first + i - from)..]);
            var b = new Vector<uint>(p[(second + i - from)..]);
            var average = Vector.ShiftRightLogical((a ^ b) & low, 1) + (a & b);
            Vector.AsVectorUInt32(Vector.AsVectorByte(new Vector<uint>(p[i..])) + Vector.AsVectorByte(average)).CopyTo(p[i..]);
        }

        for (; i < to; i++)
            p[i] = Vp8LInverseTransforms.Add(p[i], Average(p[first + i - from], p[second + i - from]));
    }

    private static void AddConstant(Span<uint> p, uint value)
    {
        var i = 0;
        var add = Vector.AsVectorByte(new Vector<uint>(value));
        for (; i + Vector<uint>.Count <= p.Length; i += Vector<uint>.Count)
            Vector.AsVectorUInt32(Vector.AsVectorByte(new Vector<uint>(p[i..])) + add).CopyTo(p[i..]);
        for (; i < p.Length; i++)
            p[i] = Vp8LInverseTransforms.Add(p[i], value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Average(uint a, uint b) => (((a ^ b) & 0xFEFEFEFE) >> 1) + (a & b);

    /// <summary>
    ///     Mode 11, Select: the left or the top pixel, whichever is nearer the gradient estimate L + T - TL.
    ///     That estimate's distance to the left pixel is the distance of T to TL, and to the top pixel that
    ///     of L to TL; the first reads only the row above, so it is computed for the whole run first.
    /// </summary>
    private static void SelectRun(Span<uint> p, int from, int to, int width)
    {
        Span<int> toLeft = stackalloc int[to - from];
        for (var i = from; i < to; i++)
            toLeft[i - from] = Distance(p[i - width], p[i - width - 1]);

        var left = p[from - 1];
        for (var i = from; i < to; i++)
        {
            var topLeft = p[i - width - 1];
            var prediction = toLeft[i - from] < Distance(left, topLeft) ? left : p[i - width];
            left = Vp8LInverseTransforms.Add(p[i], prediction);
            p[i] = left;
        }
    }

    /// <summary>The sum over the four channels of their absolute difference: one SAD instruction where there is one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Distance(uint a, uint b)
    {
        if (Sse2.IsSupported)
            return Sse2.SumAbsoluteDifferences(Vector128.CreateScalar(a).AsByte(), Vector128.CreateScalar(b).AsByte()).AsUInt16().ToScalar();
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.AddAcrossWidening(AdvSimd.AbsoluteDifference(Vector64.CreateScalar(a).AsByte(), Vector64.CreateScalar(b).AsByte())).ToScalar();
        return Math.Abs((int)(a >> 24) - (int)(b >> 24)) +
               Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF)) +
               Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF)) +
               Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF));
    }

    private static uint ClampAddSubtractFull(uint a, uint b, uint c) =>
        (Clamp((int)(a >> 24) + (int)(b >> 24) - (int)(c >> 24)) << 24) |
        (Clamp((int)((a >> 16) & 0xFF) + (int)((b >> 16) & 0xFF) - (int)((c >> 16) & 0xFF)) << 16) |
        (Clamp((int)((a >> 8) & 0xFF) + (int)((b >> 8) & 0xFF) - (int)((c >> 8) & 0xFF)) << 8) |
        Clamp((int)(a & 0xFF) + (int)(b & 0xFF) - (int)(c & 0xFF));

    private static uint ClampAddSubtractHalf(uint a, uint b) =>
        (Half((int)(a >> 24), (int)(b >> 24)) << 24) |
        (Half((int)((a >> 16) & 0xFF), (int)((b >> 16) & 0xFF)) << 16) |
        (Half((int)((a >> 8) & 0xFF), (int)((b >> 8) & 0xFF)) << 8) |
        Half((int)(a & 0xFF), (int)(b & 0xFF));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Half(int a, int b) => Clamp(a + (a - b) / 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Clamp(int value) => (uint)(value < 0 ? 0 : value > 255 ? 255 : value);
}
