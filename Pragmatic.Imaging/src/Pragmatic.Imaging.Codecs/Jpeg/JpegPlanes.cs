using System.Buffers;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     RGBA to the three planes a 4:2:0 JPEG is made of: luma at full size, each chroma averaged over
///     2×2 pixels, every plane padded to whole MCUs by repeating its last column and row.
/// </summary>
internal sealed class JpegPlanes : IDisposable
{
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

    private JpegPlanes(int width, int height)
    {
        McusPerLine = (width + 15) / 16;
        McusPerColumn = (height + 15) / 16;
        LumaStride = McusPerLine * 16;
        LumaHeight = McusPerColumn * 16;
        ChromaStride = McusPerLine * 8;
        ChromaHeight = McusPerColumn * 8;
        Luma = _pool.Rent(LumaStride * LumaHeight);
        Cb = _pool.Rent(ChromaStride * ChromaHeight);
        Cr = _pool.Rent(ChromaStride * ChromaHeight);
    }

    public int McusPerLine { get; }

    public int McusPerColumn { get; }

    public int LumaStride { get; }

    public int LumaHeight { get; }

    public int ChromaStride { get; }

    public int ChromaHeight { get; }

    public byte[] Luma { get; }

    public byte[] Cb { get; }

    public byte[] Cr { get; }

    public static JpegPlanes From(PixelBuffer image)
    {
        var planes = new JpegPlanes(image.Width, image.Height);
        var width = image.Width;
        var height = image.Height;

        // Luma, every pixel, padded to the right.
        for (var y = 0; y < planes.LumaHeight; y++)
        {
            var sourceRow = image.Row(Math.Min(y, height - 1));
            var luma = planes.Luma.AsSpan(y * planes.LumaStride, planes.LumaStride);
            var x = LumaRowAvx2(sourceRow, luma, width);
            for (; x < width; x++)
            {
                var o = x * 4;
                luma[x] = (byte)((19595 * sourceRow[o] + 38470 * sourceRow[o + 1] + 7471 * sourceRow[o + 2] + 32768) >> 16);
            }

            luma[width..].Fill(luma[width - 1]);
        }

        // Chroma from 2×2 blocks of pixels, each pixel's Cb/Cr summed then averaged.
        for (var cy = 0; cy < planes.ChromaHeight; cy++)
        {
            var row0 = image.Row(Math.Min(cy * 2, height - 1));
            var row1 = image.Row(Math.Min(cy * 2 + 1, height - 1));
            var cb = planes.Cb.AsSpan(cy * planes.ChromaStride, planes.ChromaStride);
            var cr = planes.Cr.AsSpan(cy * planes.ChromaStride, planes.ChromaStride);
            var chromaWidth = (width + 1) / 2;
            var cx = ChromaRowAvx2(row0, row1, cb, cr, width);
            for (; cx < chromaWidth; cx++)
            {
                var x0 = cx * 2 * 4;
                var x1 = Math.Min(cx * 2 + 1, width - 1) * 4;
                var r = row0[x0] + row0[x1] + row1[x0] + row1[x1];
                var g = row0[x0 + 1] + row0[x1 + 1] + row1[x0 + 1] + row1[x1 + 1];
                var b = row0[x0 + 2] + row0[x1 + 2] + row1[x0 + 2] + row1[x1 + 2];
                // Sums of four, so the 128 offset is 512 and the rounding half is 2 << 16 over the shift.
                cb[cx] = (byte)Math.Clamp((-11056 * r - 21712 * g + 32768 * b + (512 << 16) + (2 << 16)) >> 18, 0, 255);
                cr[cx] = (byte)Math.Clamp((32768 * r - 27440 * g - 5328 * b + (512 << 16) + (2 << 16)) >> 18, 0, 255);
            }

            cb[chromaWidth..].Fill(cb[chromaWidth - 1]);
            cr[chromaWidth..].Fill(cr[chromaWidth - 1]);
        }

        return planes;
    }

    /// <summary>Y of eight pixels at a time from their RGBA bytes; returns how many pixels it did.</summary>
    private static unsafe int LumaRowAvx2(ReadOnlySpan<byte> rgba, Span<byte> luma, int width)
    {
        if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported || width < 8)
            return 0;

        var count = width & ~7;
        fixed (byte* source = rgba)
        fixed (byte* destination = luma)
        {
            var mask = System.Runtime.Intrinsics.Vector256.Create(0xFFu);
            var wr = System.Runtime.Intrinsics.Vector256.Create(19595);
            var wg = System.Runtime.Intrinsics.Vector256.Create(38470);
            var wb = System.Runtime.Intrinsics.Vector256.Create(7471);
            var half = System.Runtime.Intrinsics.Vector256.Create(32768);
            for (var x = 0; x < count; x += 8)
            {
                var pixels = System.Runtime.Intrinsics.X86.Avx.LoadVector256((uint*)(source + x * 4));
                var r = System.Runtime.Intrinsics.X86.Avx2.And(pixels, mask).AsInt32();
                var g = System.Runtime.Intrinsics.X86.Avx2.And(System.Runtime.Intrinsics.X86.Avx2.ShiftRightLogical(pixels, 8), mask).AsInt32();
                var b = System.Runtime.Intrinsics.X86.Avx2.And(System.Runtime.Intrinsics.X86.Avx2.ShiftRightLogical(pixels, 16), mask).AsInt32();
                var y = System.Runtime.Intrinsics.X86.Avx2.MultiplyLow(r, wr);
                y = System.Runtime.Intrinsics.X86.Avx2.Add(y, System.Runtime.Intrinsics.X86.Avx2.MultiplyLow(g, wg));
                y = System.Runtime.Intrinsics.X86.Avx2.Add(y, System.Runtime.Intrinsics.X86.Avx2.MultiplyLow(b, wb));
                y = System.Runtime.Intrinsics.X86.Avx2.ShiftRightLogical(System.Runtime.Intrinsics.X86.Avx2.Add(y, half), 16);
                var shorts = System.Runtime.Intrinsics.X86.Sse2.PackSignedSaturate(y.GetLower(), y.GetUpper());
                var bytes = System.Runtime.Intrinsics.X86.Sse2.PackUnsignedSaturate(shorts, shorts);
                *(ulong*)(destination + x) = bytes.AsUInt64().ToScalar();
            }
        }

        return count;
    }

    /// <summary>
    ///     Cb and Cr of eight 2×2 blocks at a time: sixteen pixels of each row, summed over the rows per
    ///     channel, then over horizontal pairs. Returns how many chroma samples it did.
    /// </summary>
    private static unsafe int ChromaRowAvx2(ReadOnlySpan<byte> row0, ReadOnlySpan<byte> row1, Span<byte> cb, Span<byte> cr, int width)
    {
        if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported || width < 16)
            return 0;

        var count = (width / 2) & ~7; // Whole pairs only; an odd last pixel is the scalar tail's.
        fixed (byte* source0 = row0)
        fixed (byte* source1 = row1)
        fixed (byte* cbOut = cb)
        fixed (byte* crOut = cr)
        {
            var mask = Vector256.Create(0xFFu);
            var cbR = Vector256.Create(-11056);
            var cbG = Vector256.Create(-21712);
            var cbB = Vector256.Create(32768);
            var crR = Vector256.Create(32768);
            var crG = Vector256.Create(-27440);
            var crB = Vector256.Create(-5328);
            // Sums of four samples: the 128 offset becomes 512, the rounding half 2, both over the 18-bit shift.
            var offset = Vector256.Create((512 << 16) + (2 << 16));
            for (var cx = 0; cx < count; cx += 8)
            {
                var at = cx * 8; // Two pixels of four bytes per chroma sample.
                var a0 = System.Runtime.Intrinsics.X86.Avx.LoadVector256((uint*)(source0 + at));
                var a1 = System.Runtime.Intrinsics.X86.Avx.LoadVector256((uint*)(source0 + at + 32));
                var b0 = System.Runtime.Intrinsics.X86.Avx.LoadVector256((uint*)(source1 + at));
                var b1 = System.Runtime.Intrinsics.X86.Avx.LoadVector256((uint*)(source1 + at + 32));

                var r = PairSums(((a0 & mask) + (b0 & mask)).AsInt32(), ((a1 & mask) + (b1 & mask)).AsInt32());
                var g = PairSums((((a0 >> 8) & mask) + ((b0 >> 8) & mask)).AsInt32(), (((a1 >> 8) & mask) + ((b1 >> 8) & mask)).AsInt32());
                var b = PairSums((((a0 >> 16) & mask) + ((b0 >> 16) & mask)).AsInt32(), (((a1 >> 16) & mask) + ((b1 >> 16) & mask)).AsInt32());

                var cbValue = (r * cbR + g * cbG + b * cbB + offset) >> 18;
                var crValue = (r * crR + g * crG + b * crB + offset) >> 18;
                StoreClamped(cbValue, cbOut + cx);
                StoreClamped(crValue, crOut + cx);
            }
        }

        return count;
    }

    /// <summary>Lanes (0+1, 2+3, …, 14+15) of the sixteen values in <paramref name="low" /> then <paramref name="high" />, in order.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> PairSums(Vector256<int> low, Vector256<int> high)
    {
        // HorizontalAdd pairs within each 128-bit half: (l0+l1, l2+l3, h0+h1, h2+h3 | l4+l5, l6+l7, h4+h5, h6+h7).
        var pairs = System.Runtime.Intrinsics.X86.Avx2.HorizontalAdd(low, high);
        // Swap the middle two 64-bit quarters to put the low vector's pairs first: control (0, 2, 1, 3).
        return System.Runtime.Intrinsics.X86.Avx2.Permute4x64(pairs.AsInt64(), 0b11_01_10_00).AsInt32();
    }

    /// <summary>Eight ints clamped to 0..255 and stored as bytes: the two saturating packs do the clamp.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static unsafe void StoreClamped(Vector256<int> values, byte* destination)
    {
        var shorts = System.Runtime.Intrinsics.X86.Sse2.PackSignedSaturate(values.GetLower(), values.GetUpper());
        var bytes = System.Runtime.Intrinsics.X86.Sse2.PackUnsignedSaturate(shorts, shorts);
        *(ulong*)destination = bytes.AsUInt64().ToScalar();
    }

    public void Dispose()
    {
        _pool.Return(Luma);
        _pool.Return(Cb);
        _pool.Return(Cr);
    }
}
