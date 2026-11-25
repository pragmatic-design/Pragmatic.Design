using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The vector forms of the two inner loops of <see cref="JpegColorConverter" />: eight pixels of
///     YCbCr to RGBA at a time in 32-bit fixed point, and the 2× horizontal chroma filter sixteen
///     samples at a time in 16-bit lanes.
/// </summary>
internal static class JpegColorConverterAvx2
{
    public static bool IsSupported => Avx2.IsSupported;

    /// <summary>Converts a whole multiple of eight pixels; returns how many it did, for the scalar tail.</summary>
    public static unsafe int YCbCrToRgba(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Span<byte> rgba)
    {
        var count = y.Length & ~7;
        if (count == 0)
            return 0;

        fixed (byte* py = y)
        fixed (byte* pcb = cb)
        fixed (byte* pcr = cr)
        fixed (byte* po = rgba)
        {
            var half = Vector256.Create(1 << 15);
            var offset = Vector256.Create(128);
            var crToR = Vector256.Create(91881);
            var cbToG = Vector256.Create(22554);
            var crToG = Vector256.Create(46802);
            var cbToB = Vector256.Create(116130);
            var alpha = Vector256.Create(0xFF000000u);
            var zero = Vector256<int>.Zero;
            var max = Vector256.Create(255);

            for (var x = 0; x < count; x += 8)
            {
                var luma = Avx2.ShiftLeftLogical(Avx2.ConvertToVector256Int32(py + x), 16);
                var cbv = Avx2.Subtract(Avx2.ConvertToVector256Int32(pcb + x), offset);
                var crv = Avx2.Subtract(Avx2.ConvertToVector256Int32(pcr + x), offset);

                var r = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(luma, Avx2.MultiplyLow(crToR, crv)), half), 16);
                var g = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Subtract(Avx2.Subtract(luma, Avx2.MultiplyLow(cbToG, cbv)), Avx2.MultiplyLow(crToG, crv)), half), 16);
                var b = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(luma, Avx2.MultiplyLow(cbToB, cbv)), half), 16);

                r = Avx2.Min(Avx2.Max(r, zero), max);
                g = Avx2.Min(Avx2.Max(g, zero), max);
                b = Avx2.Min(Avx2.Max(b, zero), max);

                var pixels = Avx2.Or(Avx2.Or(r.AsUInt32(), Avx2.ShiftLeftLogical(g, 8).AsUInt32()), Avx2.Or(Avx2.ShiftLeftLogical(b, 16).AsUInt32(), alpha));
                Avx.Store((uint*)(po + x * 4), pixels);
            }
        }

        return count;
    }

    /// <summary>
    ///     out[2i] = (3·s[i] + s[i−1] + 2) &gt;&gt; 2 and out[2i+1] = (3·s[i] + s[i+1] + 1) &gt;&gt; 2 for i in
    ///     [1, pairs − 1): the interior, where both neighbours exist. Returns the first i not done.
    /// </summary>
    public static unsafe int UpsampleHorizontal2x(ReadOnlySpan<byte> source, Span<byte> output, int pairs)
    {
        // Sixteen chroma samples per step need s[i-1] .. s[i+16]: the last full step ends at i + 16 < pairs.
        var i = 1;
        if (pairs < 18)
            return i;

        fixed (byte* s = source)
        fixed (byte* o = output)
        {
            var three = Vector256.Create((ushort)3);
            var one = Vector256.Create((ushort)1);
            var two = Vector256.Create((ushort)2);
            for (; i + 16 < pairs; i += 16)
            {
                var centre = Avx2.ConvertToVector256Int16(s + i).AsUInt16();
                var left = Avx2.ConvertToVector256Int16(s + i - 1).AsUInt16();
                var right = Avx2.ConvertToVector256Int16(s + i + 1).AsUInt16();
                var weighted = Avx2.MultiplyLow(centre, three);
                var even = Avx2.ShiftRightLogical(Avx2.Add(Avx2.Add(weighted, left), two), 2);
                var odd = Avx2.ShiftRightLogical(Avx2.Add(Avx2.Add(weighted, right), one), 2);

                // Interleave even and odd samples. The packs work per 128-bit half, so the low half of
                // each holds samples 0..7 and the high half 8..15; unpacking the low bytes of the two
                // halves gives pairs 0..7 in the low half and 8..15 in the high half: already in order.
                var evenBytes = Avx2.PackUnsignedSaturate(even.AsInt16(), even.AsInt16());
                var oddBytes = Avx2.PackUnsignedSaturate(odd.AsInt16(), odd.AsInt16());
                var interleaved = Avx2.UnpackLow(evenBytes, oddBytes);
                Avx.Store(o + i * 2, interleaved);
            }
        }

        return i;
    }
}
