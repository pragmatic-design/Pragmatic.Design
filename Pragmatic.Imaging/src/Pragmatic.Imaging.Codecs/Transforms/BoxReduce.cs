using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     Halves an image: every output pixel is the rounded mean of a 2×2 block, the step that takes a large
///     image most of the way to a small one at a fraction of a kernel's cost. The vertical sum is a
///     widening add of two rows; the horizontal one pairs adjacent pixels with AVX2 unpacks where the
///     hardware has them.
/// </summary>
internal static class BoxReduce
{
    public static PixelBuffer Half(PixelBuffer source)
    {
        var width = (source.Width + 1) / 2;
        var height = (source.Height + 1) / 2;
        var destination = new PixelBuffer(width, height);
        var sums = ArrayPool<ushort>.Shared.Rent(source.Width * 4);
        try
        {
            var rowSums = sums.AsSpan(0, source.Width * 4);
            for (var y = 0; y < height; y++)
            {
                var row0 = source.Row(y * 2);
                var row1 = source.Row(Math.Min(y * 2 + 1, source.Height - 1));
                SumRows(row0, row1, rowSums);
                PairPixels(rowSums, source.Width, destination.Row(y));
            }

            return destination;
        }
        catch
        {
            destination.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(sums);
        }
    }

    /// <summary>sums[i] = a[i] + b[i], widened.</summary>
    private static void SumRows(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<ushort> sums)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<byte>.Count)
        {
            var va = MemoryMarshal.Cast<byte, Vector<byte>>(a);
            var vb = MemoryMarshal.Cast<byte, Vector<byte>>(b);
            var vs = MemoryMarshal.Cast<ushort, Vector<ushort>>(sums);
            var o = 0;
            for (var v = 0; v < va.Length; v++)
            {
                Vector.Widen(va[v], out var aLow, out var aHigh);
                Vector.Widen(vb[v], out var bLow, out var bHigh);
                vs[o++] = aLow + bLow;
                vs[o++] = aHigh + bHigh;
            }

            i = va.Length * Vector<byte>.Count;
        }

        for (; i < a.Length; i++)
            sums[i] = (ushort)(a[i] + b[i]);
    }

    /// <summary>out pixel j = (sums of pixel 2j + sums of pixel 2j+1 + 2) &gt;&gt; 2, per channel.</summary>
    private static void PairPixels(ReadOnlySpan<ushort> sums, int sourceWidth, Span<byte> output)
    {
        var outputPixels = output.Length / 4;
        var fullPairs = sourceWidth / 2;
        var j = 0;

        if (Avx2.IsSupported && fullPairs >= 4)
        {
            unsafe
            {
                fixed (ushort* s = sums)
                fixed (byte* o = output)
                {
                    var two = Vector256.Create((ushort)2);
                    // Eight source pixels (32 ushorts) make four output pixels (16 bytes) per step.
                    for (; j + 4 <= fullPairs; j += 4)
                    {
                        var a = Avx.LoadVector256(s + j * 8); // pixels 2j .. 2j+3
                        var b = Avx.LoadVector256(s + j * 8 + 16); // pixels 2j+4 .. 2j+7
                        var evens = Avx2.UnpackLow(a.AsUInt64(), b.AsUInt64()); // A0 B0 A2 B2
                        var odds = Avx2.UnpackHigh(a.AsUInt64(), b.AsUInt64()); // A1 B1 A3 B3
                        var pairs = Avx2.Add(evens.AsUInt16(), odds.AsUInt16()); // A0+A1 B0+B1 A2+A3 B2+B3
                        var ordered = Avx2.Permute4x64(pairs.AsUInt64(), 0xD8).AsUInt16(); // A01 A23 B01 B23
                        var mean = Avx2.ShiftRightLogical(Avx2.Add(ordered, two), 2);
                        var packed = Avx2.PackUnsignedSaturate(mean.AsInt16(), Vector256<short>.Zero); // lane0: 8 bytes, lane1: 8 bytes
                        var bytes = Avx2.Permute4x64(packed.AsUInt64(), 0x08).GetLower().AsByte(); // the two 8-byte halves together
                        bytes.Store(o + j * 4);
                    }
                }
            }
        }

        for (; j < outputPixels; j++)
        {
            var x0 = j * 2 * 4;
            var x1 = Math.Min(j * 2 + 1, sourceWidth - 1) * 4;
            var o = j * 4;
            output[o] = (byte)((sums[x0] + sums[x1] + 2) >> 2);
            output[o + 1] = (byte)((sums[x0 + 1] + sums[x1 + 1] + 2) >> 2);
            output[o + 2] = (byte)((sums[x0 + 2] + sums[x1 + 2] + 2) >> 2);
            output[o + 3] = (byte)((sums[x0 + 3] + sums[x1 + 3] + 2) >> 2);
        }
    }
}
