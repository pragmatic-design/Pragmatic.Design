using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The intra predictions of a VP8 key frame (RFC 6386, section 12), written into a work buffer whose
///     row above and column to the left of the block hold its neighbours: <c>dst - Stride</c> is the row
///     above, <c>dst - 1</c> the column to the left, and the four bytes after the row above, the pixels
///     above and to the right. The arithmetic is libwebp's.
/// </summary>
internal static class Vp8Prediction
{
    public const int Stride = 32;

    /// <summary>A 16 x 16 or 8 x 8 block predicted whole. <paramref name="hasTop" /> and <paramref name="hasLeft" /> matter only for DC.</summary>
    public static void Block(byte[] buffer, int dst, int size, int mode, bool hasTop, bool hasLeft)
    {
        switch (mode)
        {
            case Vp8Modes.V:
                for (var y = 0; y < size; y++)
                    Array.Copy(buffer, dst - Stride, buffer, dst + y * Stride, size);
                return;
            case Vp8Modes.H:
                for (var y = 0; y < size; y++)
                    buffer.AsSpan(dst + y * Stride, size).Fill(buffer[dst + y * Stride - 1]);
                return;
            case Vp8Modes.Tm:
                TrueMotion(buffer, dst, size);
                return;
            default:
                Fill(buffer, dst, size, Dc(buffer, dst, size, hasTop, hasLeft));
                return;
        }
    }

    private static byte Dc(byte[] buffer, int dst, int size, bool hasTop, bool hasLeft)
    {
        var shift = size == 16 ? 4 : 3;
        var sum = 0;
        if (hasTop)
        {
            for (var i = 0; i < size; i++)
                sum += buffer[dst - Stride + i];
        }

        if (hasLeft)
        {
            for (var i = 0; i < size; i++)
                sum += buffer[dst + i * Stride - 1];
        }

        if (hasTop && hasLeft)
            return (byte)((sum + size) >> (shift + 1));
        if (hasTop || hasLeft)
            return (byte)((sum + (size >> 1)) >> shift);
        return 0x80;
    }

    private static void Fill(byte[] buffer, int dst, int size, byte value)
    {
        for (var y = 0; y < size; y++)
            buffer.AsSpan(dst + y * Stride, size).Fill(value);
    }

    private static void TrueMotion(byte[] buffer, int dst, int size)
    {
        if (size >= 8 && Vector128.IsHardwareAccelerated)
        {
            TrueMotionVector(buffer, dst, size);
            return;
        }

        var topLeft = buffer[dst - Stride - 1];
        for (var y = 0; y < size; y++)
        {
            var left = buffer[dst + y * Stride - 1] - topLeft;
            for (var x = 0; x < size; x++)
                buffer[dst + y * Stride + x] = Clip(buffer[dst - Stride + x] + left);
        }
    }

    /// <summary>The same for 8 or 16 wide: the row above widened once, each row's offset added and clamped.</summary>
    private static void TrueMotionVector(byte[] buffer, int dst, int size)
    {
        if (dst - Stride - 1 < 0 || buffer.Length < dst + (size - 1) * Stride + 16)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var origin = ref MemoryMarshal.GetArrayDataReference(buffer);
        var top = Vector128.LoadUnsafe(ref origin, (nuint)(dst - Stride));
        var topLow = Vector128.WidenLower(top).AsInt16();
        var topHigh = Vector128.WidenUpper(top).AsInt16();
        int topLeft = buffer[dst - Stride - 1];
        for (var y = 0; y < size; y++)
        {
            var row = dst + y * Stride;
            var offset = Vector128.Create((short)(buffer[row - 1] - topLeft));
            var predicted = Vector128.Narrow(Clamp(topLow + offset), Clamp(topHigh + offset));
            if (size == 16)
                predicted.StoreUnsafe(ref origin, (nuint)row);
            else
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref origin, row), predicted.AsUInt64().ToScalar());
        }
    }

    private static Vector128<ushort> Clamp(Vector128<short> value) =>
        Vector128.Min(Vector128.Max(value, Vector128<short>.Zero), Vector128.Create((short)255)).AsUInt16();

    /// <summary>
    ///     A 4 x 4 luma block predicted by one of the ten subblock modes. Each row is assembled in a register
    ///     and written whole: the transform reads it back a row at a time, and four byte stores would keep that
    ///     read waiting for all of them to land.
    /// </summary>
    public static void Subblock(byte[] b, int dst, int mode)
    {
        const int s = Stride;

        // The block reads one row above (and eight across it) and one column to its left: checked once, then
        // read and written through a reference.
        if (dst - s - 1 < 0 || dst + 3 * s + 4 > b.Length || dst - s + 8 > b.Length)
            throw new ArgumentOutOfRangeException(nameof(dst), dst, "The block and its neighbours reach past the buffer.");
        ref var o = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(b), dst);

        switch (mode)
        {
            case Vp8Modes.BVe:
            {
                var row = Pack(
                    Avg3(Top(ref o, -1), Top(ref o, 0), Top(ref o, 1)),
                    Avg3(Top(ref o, 0), Top(ref o, 1), Top(ref o, 2)),
                    Avg3(Top(ref o, 1), Top(ref o, 2), Top(ref o, 3)),
                    Avg3(Top(ref o, 2), Top(ref o, 3), Top(ref o, 4)));
                Store(ref o, row, row, row, row);
                return;
            }
            case Vp8Modes.BHe:
            {
                int a = Top(ref o, -1), bb = Left(ref o, 0), c = Left(ref o, 1), d = Left(ref o, 2), e = Left(ref o, 3);
                Store(ref o, Splat(Avg3(a, bb, c)), Splat(Avg3(bb, c, d)), Splat(Avg3(c, d, e)), Splat(Avg3(d, e, e)));
                return;
            }
            case Vp8Modes.BTm:
                TrueMotion4(ref o);
                return;
            case Vp8Modes.BDc:
            {
                var sum = 4;
                for (var i = 0; i < 4; i++)
                    sum += Top(ref o, i) + Left(ref o, i);
                var row = Splat(sum >> 3);
                Store(ref o, row, row, row, row);
                return;
            }
            case Vp8Modes.BRd:
            {
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), l = Left(ref o, 3), x = Top(ref o, -1), a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3);

                // One value per down-right diagonal, from the bottom-left corner to the top-right one.
                int e0 = Avg3(j, k, l), e1 = Avg3(i, j, k), e2 = Avg3(x, i, j), e3 = Avg3(a, x, i), e4 = Avg3(bb, a, x), e5 = Avg3(c, bb, a), e6 = Avg3(d, c, bb);
                Store(ref o, Pack(e3, e4, e5, e6), Pack(e2, e3, e4, e5), Pack(e1, e2, e3, e4), Pack(e0, e1, e2, e3));
                return;
            }
            case Vp8Modes.BLd:
            {
                int a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3), e = Top(ref o, 4), f = Top(ref o, 5), g = Top(ref o, 6), h = Top(ref o, 7);

                // One value per down-left diagonal, from the top-left corner to the bottom-right one.
                int f0 = Avg3(a, bb, c), f1 = Avg3(bb, c, d), f2 = Avg3(c, d, e), f3 = Avg3(d, e, f), f4 = Avg3(e, f, g), f5 = Avg3(f, g, h), f6 = Avg3(g, h, h);
                Store(ref o, Pack(f0, f1, f2, f3), Pack(f1, f2, f3, f4), Pack(f2, f3, f4, f5), Pack(f3, f4, f5, f6));
                return;
            }
            case Vp8Modes.BVr:
            {
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), x = Top(ref o, -1), a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3);
                int xa = Avg2(x, a), ab = Avg2(a, bb), bc = Avg2(bb, c), cd = Avg2(c, d);
                int kji = Avg3(k, j, i), jix = Avg3(j, i, x), ixa = Avg3(i, x, a), xab = Avg3(x, a, bb), abc = Avg3(a, bb, c), bcd = Avg3(bb, c, d);
                Store(ref o, Pack(xa, ab, bc, cd), Pack(ixa, xab, abc, bcd), Pack(jix, xa, ab, bc), Pack(kji, ixa, xab, abc));
                return;
            }
            case Vp8Modes.BVl:
            {
                int a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3), e = Top(ref o, 4), f = Top(ref o, 5), g = Top(ref o, 6), h = Top(ref o, 7);
                int ab = Avg2(a, bb), bc = Avg2(bb, c), cd = Avg2(c, d), de = Avg2(d, e);
                int abc = Avg3(a, bb, c), bcd = Avg3(bb, c, d), cde = Avg3(c, d, e), def = Avg3(d, e, f), efg = Avg3(e, f, g), fgh = Avg3(f, g, h);
                Store(ref o, Pack(ab, bc, cd, de), Pack(abc, bcd, cde, def), Pack(bc, cd, de, efg), Pack(bcd, cde, def, fgh));
                return;
            }
            case Vp8Modes.BHd:
            {
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), l = Left(ref o, 3), x = Top(ref o, -1), a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2);
                int ix = Avg2(i, x), ji = Avg2(j, i), kj = Avg2(k, j), lk = Avg2(l, k);
                int ixa = Avg3(i, x, a), jix = Avg3(j, i, x), kji = Avg3(k, j, i), lkj = Avg3(l, k, j), xab = Avg3(x, a, bb), abc = Avg3(a, bb, c);
                Store(ref o, Pack(ix, ixa, xab, abc), Pack(ji, jix, ix, ixa), Pack(kj, kji, ji, jix), Pack(lk, lkj, kj, kji));
                return;
            }
            default:
            {
                // Horizontal-up.
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), l = Left(ref o, 3);
                int ij = Avg2(i, j), jk = Avg2(j, k), kl = Avg2(k, l);
                int ijk = Avg3(i, j, k), jkl = Avg3(j, k, l), kll = Avg3(k, l, l);
                Store(ref o, Pack(ij, ijk, jk, jkl), Pack(jk, jkl, kl, kll), Pack(kl, kll, l, l), Splat(l));
                return;
            }
        }
    }

    /// <summary>
    ///     All ten subblock modes of the block at <paramref name="dst" /> at once, each as its four row words
    ///     (mode <c>m</c>'s rows at <c>4 m</c>) for <see cref="Put" /> to write: the encoder tries every mode
    ///     on the same neighbours, which are read once and whose averages the modes share are taken once, as
    ///     libwebp's <c>MakeIntra4Preds</c> does. Each mode's rows are <see cref="Subblock" />'s.
    /// </summary>
    public static void Subblocks(byte[] b, int dst, Span<uint> rows)
    {
        const int s = Stride;
        if (dst - s - 1 < 0 || dst + 3 * s + 4 > b.Length || dst - s + 8 > b.Length || rows.Length < 40)
            throw new ArgumentOutOfRangeException(nameof(dst), dst, "The block and its neighbours reach past the buffer.");
        ref var o = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(b), dst);
        ref var r = ref MemoryMarshal.GetReference(rows);

        int x = Top(ref o, -1), a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3);
        int e = Top(ref o, 4), f = Top(ref o, 5), g = Top(ref o, 6), h = Top(ref o, 7);
        int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), l = Left(ref o, 3);

        // The averages more than one mode takes.
        int xab = Avg3(x, a, bb), abc = Avg3(a, bb, c), bcd = Avg3(bb, c, d), cde = Avg3(c, d, e);
        int def = Avg3(d, e, f), efg = Avg3(e, f, g), fgh = Avg3(f, g, h);
        int ixa = Avg3(i, x, a), jix = Avg3(j, i, x), kji = Avg3(k, j, i), lkj = Avg3(l, k, j), kll = Avg3(k, l, l);
        int xa = Avg2(x, a), ab = Avg2(a, bb), bc = Avg2(bb, c), cd = Avg2(c, d), de = Avg2(d, e);
        int ix = Avg2(i, x), ji = Avg2(j, i), kj = Avg2(k, j), lk = Avg2(l, k);

        var dc = Splat((a + bb + c + d + i + j + k + l + 4) >> 3);
        Set(ref r, Vp8Modes.BDc, dc, dc, dc, dc);
        TrueMotion4Rows(ref o, ref Unsafe.Add(ref r, Vp8Modes.BTm * 4));
        var ve = Pack(xab, abc, bcd, cde);
        Set(ref r, Vp8Modes.BVe, ve, ve, ve, ve);
        Set(ref r, Vp8Modes.BHe, Splat(Avg3(x, i, j)), Splat(Avg3(i, j, k)), Splat(Avg3(j, k, l)), Splat(kll));

        // Down-right: one value per diagonal, bottom-left corner to top-right.
        int e0 = Avg3(j, k, l), e1 = Avg3(i, j, k);
        Set(ref r, Vp8Modes.BRd, Pack(ixa, xab, abc, bcd), Pack(jix, ixa, xab, abc), Pack(e1, jix, ixa, xab), Pack(e0, e1, jix, ixa));

        // Down-left: one value per diagonal, top-left corner to bottom-right.
        var ghh = Avg3(g, h, h);
        Set(ref r, Vp8Modes.BLd, Pack(abc, bcd, cde, def), Pack(bcd, cde, def, efg), Pack(cde, def, efg, fgh), Pack(def, efg, fgh, ghh));

        Set(ref r, Vp8Modes.BVr, Pack(xa, ab, bc, cd), Pack(ixa, xab, abc, bcd), Pack(jix, xa, ab, bc), Pack(kji, ixa, xab, abc));
        Set(ref r, Vp8Modes.BVl, Pack(ab, bc, cd, de), Pack(abc, bcd, cde, def), Pack(bc, cd, de, efg), Pack(bcd, cde, def, fgh));
        Set(ref r, Vp8Modes.BHd, Pack(ix, ixa, xab, abc), Pack(ji, jix, ix, ixa), Pack(kj, kji, ji, jix), Pack(lk, lkj, kj, kji));

        int ij = Avg2(i, j), jk = Avg2(j, k), kl = Avg2(k, l), ijk = e1, jkl = e0;
        Set(ref r, Vp8Modes.BHu, Pack(ij, ijk, jk, jkl), Pack(jk, jkl, kl, kll), Pack(kl, kll, l, l), Splat(l));
    }

    /// <summary>Writes one mode's four row words, as <see cref="Subblocks" /> gives them, to the block at <paramref name="dst" />.</summary>
    public static void Put(byte[] b, int dst, ReadOnlySpan<uint> rows)
    {
        if (dst + 3 * Stride + 4 > b.Length || rows.Length < 4)
            throw new ArgumentOutOfRangeException(nameof(dst), dst, "The block reaches past the buffer.");
        ref var r = ref MemoryMarshal.GetReference(rows);
        Store(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(b), dst), r, Unsafe.Add(ref r, 1), Unsafe.Add(ref r, 2), Unsafe.Add(ref r, 3));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Set(ref uint rows, int mode, uint row0, uint row1, uint row2, uint row3)
    {
        ref var at = ref Unsafe.Add(ref rows, mode * 4);
        at = row0;
        Unsafe.Add(ref at, 1) = row1;
        Unsafe.Add(ref at, 2) = row2;
        Unsafe.Add(ref at, 3) = row3;
    }

    /// <summary>True motion's four row words, as <see cref="TrueMotion4" /> writes them.</summary>
    private static void TrueMotion4Rows(ref byte o, ref uint rows)
    {
        int topLeft = Top(ref o, -1);
        if (Vector128.IsHardwareAccelerated)
        {
            var above = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref o, -Stride))).AsByte()).AsInt16();
            var top = Vector128.Shuffle(above, Vector128.Create((short)0, 1, 2, 3, 0, 1, 2, 3));
            short l0 = (short)(Left(ref o, 0) - topLeft), l1 = (short)(Left(ref o, 1) - topLeft);
            short l2 = (short)(Left(ref o, 2) - topLeft), l3 = (short)(Left(ref o, 3) - topLeft);
            Vector128.Narrow(
                Clamp(top + Vector128.Create(l0, l0, l0, l0, l1, l1, l1, l1)),
                Clamp(top + Vector128.Create(l2, l2, l2, l2, l3, l3, l3, l3))).AsUInt32().StoreUnsafe(ref rows);
            return;
        }

        int t0 = Top(ref o, 0), t1 = Top(ref o, 1), t2 = Top(ref o, 2), t3 = Top(ref o, 3);
        for (var y = 0; y < 4; y++)
        {
            var left = Left(ref o, y) - topLeft;
            Unsafe.Add(ref rows, y) = Pack(Clip(t0 + left), Clip(t1 + left), Clip(t2 + left), Clip(t3 + left));
        }
    }

    /// <summary>True motion over a 4 x 4 block: the row above plus each row's left neighbour less the corner, clamped.</summary>
    private static void TrueMotion4(ref byte o)
    {
        if (Vector128.IsHardwareAccelerated)
            TrueMotion4Vector(ref o);
        else
            TrueMotion4Scalar(ref o);
    }

    /// <summary>The same, two rows to a vector of eight: the row above twice, and each half its own row's offset.</summary>
    internal static void TrueMotion4Vector(ref byte o)
    {
        int topLeft = Top(ref o, -1);
        var above = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref o, -Stride))).AsByte()).AsInt16();
        var top = Vector128.Shuffle(above, Vector128.Create((short)0, 1, 2, 3, 0, 1, 2, 3));
        short l0 = (short)(Left(ref o, 0) - topLeft), l1 = (short)(Left(ref o, 1) - topLeft);
        short l2 = (short)(Left(ref o, 2) - topLeft), l3 = (short)(Left(ref o, 3) - topLeft);
        var rows = Vector128.Narrow(
            Clamp(top + Vector128.Create(l0, l0, l0, l0, l1, l1, l1, l1)),
            Clamp(top + Vector128.Create(l2, l2, l2, l2, l3, l3, l3, l3))).AsUInt32();
        Store(ref o, rows.GetElement(0), rows.GetElement(1), rows.GetElement(2), rows.GetElement(3));
    }

    internal static void TrueMotion4Scalar(ref byte o)
    {
        int topLeft = Top(ref o, -1);
        int t0 = Top(ref o, 0), t1 = Top(ref o, 1), t2 = Top(ref o, 2), t3 = Top(ref o, 3);
        int l0 = Left(ref o, 0) - topLeft, l1 = Left(ref o, 1) - topLeft, l2 = Left(ref o, 2) - topLeft, l3 = Left(ref o, 3) - topLeft;
        Store(ref o,
            Pack(Clip(t0 + l0), Clip(t1 + l0), Clip(t2 + l0), Clip(t3 + l0)),
            Pack(Clip(t0 + l1), Clip(t1 + l1), Clip(t2 + l1), Clip(t3 + l1)),
            Pack(Clip(t0 + l2), Clip(t1 + l2), Clip(t2 + l2), Clip(t3 + l2)),
            Pack(Clip(t0 + l3), Clip(t1 + l3), Clip(t2 + l3), Clip(t3 + l3)));
    }

    /// <summary>Four pixels, left to right, as the row word they make in memory.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Pack(int p0, int p1, int p2, int p3)
    {
        var word = (uint)(p0 | (p1 << 8) | (p2 << 16) | (p3 << 24));
        return BitConverter.IsLittleEndian ? word : BinaryPrimitives.ReverseEndianness(word);
    }

    /// <summary>A row of four of one value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Splat(int value) => (uint)value * 0x01010101u;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(ref byte o, uint row0, uint row1, uint row2, uint row3)
    {
        Unsafe.WriteUnaligned(ref o, row0);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, Stride), row1);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, 2 * Stride), row2);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, 3 * Stride), row3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Top(ref byte o, int i) => Unsafe.Add(ref o, i - Stride);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Left(ref byte o, int i) => Unsafe.Add(ref o, i * Stride - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Set(ref byte o, int x, int y, int value) => Unsafe.Add(ref o, x + y * Stride) = (byte)value;

    private static int Avg3(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;

    private static int Avg2(int a, int b) => (a + b + 1) >> 1;

    private static byte Clip(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
