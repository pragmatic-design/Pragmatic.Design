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

    /// <summary>A 4 x 4 luma block predicted by one of the ten subblock modes.</summary>
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
                for (var x = 0; x < 4; x++)
                {
                    var value = Avg3(Top(ref o, x - 1), Top(ref o, x), Top(ref o, x + 1));
                    for (var y = 0; y < 4; y++)
                        Set(ref o, x, y, value);
                }

                return;
            }
            case Vp8Modes.BHe:
            {
                int a = Top(ref o, -1), bb = Left(ref o, 0), c = Left(ref o, 1), d = Left(ref o, 2), e = Left(ref o, 3);
                b.AsSpan(dst, 4).Fill((byte)Avg3(a, bb, c));
                b.AsSpan(dst + s, 4).Fill((byte)Avg3(bb, c, d));
                b.AsSpan(dst + 2 * s, 4).Fill((byte)Avg3(c, d, e));
                b.AsSpan(dst + 3 * s, 4).Fill((byte)Avg3(d, e, e));
                return;
            }
            case Vp8Modes.BTm:
                TrueMotion(b, dst, 4);
                return;
            case Vp8Modes.BDc:
            {
                var sum = 4;
                for (var i = 0; i < 4; i++)
                    sum += Top(ref o, i) + Left(ref o, i);
                Fill(b, dst, 4, (byte)(sum >> 3));
                return;
            }
            case Vp8Modes.BRd:
            {
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), l = Left(ref o, 3), x = Top(ref o, -1), a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3);
                Set(ref o, 0, 3, Avg3(j, k, l));
                Set(ref o, 1, 3, Avg3(i, j, k)); Set(ref o, 0, 2, Avg3(i, j, k));
                Set(ref o, 2, 3, Avg3(x, i, j)); Set(ref o, 1, 2, Avg3(x, i, j)); Set(ref o, 0, 1, Avg3(x, i, j));
                Set(ref o, 3, 3, Avg3(a, x, i)); Set(ref o, 2, 2, Avg3(a, x, i)); Set(ref o, 1, 1, Avg3(a, x, i)); Set(ref o, 0, 0, Avg3(a, x, i));
                Set(ref o, 3, 2, Avg3(bb, a, x)); Set(ref o, 2, 1, Avg3(bb, a, x)); Set(ref o, 1, 0, Avg3(bb, a, x));
                Set(ref o, 3, 1, Avg3(c, bb, a)); Set(ref o, 2, 0, Avg3(c, bb, a));
                Set(ref o, 3, 0, Avg3(d, c, bb));
                return;
            }
            case Vp8Modes.BLd:
            {
                int a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3), e = Top(ref o, 4), f = Top(ref o, 5), g = Top(ref o, 6), h = Top(ref o, 7);
                Set(ref o, 0, 0, Avg3(a, bb, c));
                Set(ref o, 1, 0, Avg3(bb, c, d)); Set(ref o, 0, 1, Avg3(bb, c, d));
                Set(ref o, 2, 0, Avg3(c, d, e)); Set(ref o, 1, 1, Avg3(c, d, e)); Set(ref o, 0, 2, Avg3(c, d, e));
                Set(ref o, 3, 0, Avg3(d, e, f)); Set(ref o, 2, 1, Avg3(d, e, f)); Set(ref o, 1, 2, Avg3(d, e, f)); Set(ref o, 0, 3, Avg3(d, e, f));
                Set(ref o, 3, 1, Avg3(e, f, g)); Set(ref o, 2, 2, Avg3(e, f, g)); Set(ref o, 1, 3, Avg3(e, f, g));
                Set(ref o, 3, 2, Avg3(f, g, h)); Set(ref o, 2, 3, Avg3(f, g, h));
                Set(ref o, 3, 3, Avg3(g, h, h));
                return;
            }
            case Vp8Modes.BVr:
            {
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), x = Top(ref o, -1), a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3);
                Set(ref o, 0, 0, Avg2(x, a)); Set(ref o, 1, 2, Avg2(x, a));
                Set(ref o, 1, 0, Avg2(a, bb)); Set(ref o, 2, 2, Avg2(a, bb));
                Set(ref o, 2, 0, Avg2(bb, c)); Set(ref o, 3, 2, Avg2(bb, c));
                Set(ref o, 3, 0, Avg2(c, d));
                Set(ref o, 0, 3, Avg3(k, j, i));
                Set(ref o, 0, 2, Avg3(j, i, x));
                Set(ref o, 0, 1, Avg3(i, x, a)); Set(ref o, 1, 3, Avg3(i, x, a));
                Set(ref o, 1, 1, Avg3(x, a, bb)); Set(ref o, 2, 3, Avg3(x, a, bb));
                Set(ref o, 2, 1, Avg3(a, bb, c)); Set(ref o, 3, 3, Avg3(a, bb, c));
                Set(ref o, 3, 1, Avg3(bb, c, d));
                return;
            }
            case Vp8Modes.BVl:
            {
                int a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2), d = Top(ref o, 3), e = Top(ref o, 4), f = Top(ref o, 5), g = Top(ref o, 6), h = Top(ref o, 7);
                Set(ref o, 0, 0, Avg2(a, bb));
                Set(ref o, 1, 0, Avg2(bb, c)); Set(ref o, 0, 2, Avg2(bb, c));
                Set(ref o, 2, 0, Avg2(c, d)); Set(ref o, 1, 2, Avg2(c, d));
                Set(ref o, 3, 0, Avg2(d, e)); Set(ref o, 2, 2, Avg2(d, e));
                Set(ref o, 0, 1, Avg3(a, bb, c));
                Set(ref o, 1, 1, Avg3(bb, c, d)); Set(ref o, 0, 3, Avg3(bb, c, d));
                Set(ref o, 2, 1, Avg3(c, d, e)); Set(ref o, 1, 3, Avg3(c, d, e));
                Set(ref o, 3, 1, Avg3(d, e, f)); Set(ref o, 2, 3, Avg3(d, e, f));
                Set(ref o, 3, 2, Avg3(e, f, g));
                Set(ref o, 3, 3, Avg3(f, g, h));
                return;
            }
            case Vp8Modes.BHd:
            {
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), l = Left(ref o, 3), x = Top(ref o, -1), a = Top(ref o, 0), bb = Top(ref o, 1), c = Top(ref o, 2);
                Set(ref o, 0, 0, Avg2(i, x)); Set(ref o, 2, 1, Avg2(i, x));
                Set(ref o, 0, 1, Avg2(j, i)); Set(ref o, 2, 2, Avg2(j, i));
                Set(ref o, 0, 2, Avg2(k, j)); Set(ref o, 2, 3, Avg2(k, j));
                Set(ref o, 0, 3, Avg2(l, k));
                Set(ref o, 3, 0, Avg3(a, bb, c));
                Set(ref o, 2, 0, Avg3(x, a, bb));
                Set(ref o, 1, 0, Avg3(i, x, a)); Set(ref o, 3, 1, Avg3(i, x, a));
                Set(ref o, 1, 1, Avg3(j, i, x)); Set(ref o, 3, 2, Avg3(j, i, x));
                Set(ref o, 1, 2, Avg3(k, j, i)); Set(ref o, 3, 3, Avg3(k, j, i));
                Set(ref o, 1, 3, Avg3(l, k, j));
                return;
            }
            default:
            {
                // Horizontal-up.
                int i = Left(ref o, 0), j = Left(ref o, 1), k = Left(ref o, 2), l = Left(ref o, 3);
                Set(ref o, 0, 0, Avg2(i, j));
                Set(ref o, 2, 0, Avg2(j, k)); Set(ref o, 0, 1, Avg2(j, k));
                Set(ref o, 2, 1, Avg2(k, l)); Set(ref o, 0, 2, Avg2(k, l));
                Set(ref o, 1, 0, Avg3(i, j, k));
                Set(ref o, 3, 0, Avg3(j, k, l)); Set(ref o, 1, 1, Avg3(j, k, l));
                Set(ref o, 3, 1, Avg3(k, l, l)); Set(ref o, 1, 2, Avg3(k, l, l));
                Set(ref o, 3, 2, l); Set(ref o, 2, 2, l); Set(ref o, 0, 3, l); Set(ref o, 1, 3, l); Set(ref o, 2, 3, l); Set(ref o, 3, 3, l);
                return;
            }
        }
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
