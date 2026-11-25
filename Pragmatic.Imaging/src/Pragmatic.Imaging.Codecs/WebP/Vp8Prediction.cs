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
        var topLeft = buffer[dst - Stride - 1];
        for (var y = 0; y < size; y++)
        {
            var left = buffer[dst + y * Stride - 1] - topLeft;
            for (var x = 0; x < size; x++)
                buffer[dst + y * Stride + x] = Clip(buffer[dst - Stride + x] + left);
        }
    }

    /// <summary>A 4 x 4 luma block predicted by one of the ten subblock modes.</summary>
    public static void Subblock(byte[] b, int dst, int mode)
    {
        const int s = Stride;
        int Top(int i) => b[dst - s + i];
        int Left(int i) => b[dst - 1 + i * s];
        void Set(int x, int y, int value) => b[dst + x + y * s] = (byte)value;

        switch (mode)
        {
            case Vp8Modes.BVe:
            {
                for (var x = 0; x < 4; x++)
                {
                    var value = Avg3(Top(x - 1), Top(x), Top(x + 1));
                    for (var y = 0; y < 4; y++)
                        Set(x, y, value);
                }

                return;
            }
            case Vp8Modes.BHe:
            {
                int a = Top(-1), bb = Left(0), c = Left(1), d = Left(2), e = Left(3);
                int[] rows = [Avg3(a, bb, c), Avg3(bb, c, d), Avg3(c, d, e), Avg3(d, e, e)];
                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < 4; x++)
                        Set(x, y, rows[y]);
                }

                return;
            }
            case Vp8Modes.BTm:
                TrueMotion(b, dst, 4);
                return;
            case Vp8Modes.BDc:
            {
                var sum = 4;
                for (var i = 0; i < 4; i++)
                    sum += Top(i) + Left(i);
                Fill(b, dst, 4, (byte)(sum >> 3));
                return;
            }
            case Vp8Modes.BRd:
            {
                int i = Left(0), j = Left(1), k = Left(2), l = Left(3), x = Top(-1), a = Top(0), bb = Top(1), c = Top(2), d = Top(3);
                Set(0, 3, Avg3(j, k, l));
                Set(1, 3, Avg3(i, j, k)); Set(0, 2, Avg3(i, j, k));
                Set(2, 3, Avg3(x, i, j)); Set(1, 2, Avg3(x, i, j)); Set(0, 1, Avg3(x, i, j));
                Set(3, 3, Avg3(a, x, i)); Set(2, 2, Avg3(a, x, i)); Set(1, 1, Avg3(a, x, i)); Set(0, 0, Avg3(a, x, i));
                Set(3, 2, Avg3(bb, a, x)); Set(2, 1, Avg3(bb, a, x)); Set(1, 0, Avg3(bb, a, x));
                Set(3, 1, Avg3(c, bb, a)); Set(2, 0, Avg3(c, bb, a));
                Set(3, 0, Avg3(d, c, bb));
                return;
            }
            case Vp8Modes.BLd:
            {
                int a = Top(0), bb = Top(1), c = Top(2), d = Top(3), e = Top(4), f = Top(5), g = Top(6), h = Top(7);
                Set(0, 0, Avg3(a, bb, c));
                Set(1, 0, Avg3(bb, c, d)); Set(0, 1, Avg3(bb, c, d));
                Set(2, 0, Avg3(c, d, e)); Set(1, 1, Avg3(c, d, e)); Set(0, 2, Avg3(c, d, e));
                Set(3, 0, Avg3(d, e, f)); Set(2, 1, Avg3(d, e, f)); Set(1, 2, Avg3(d, e, f)); Set(0, 3, Avg3(d, e, f));
                Set(3, 1, Avg3(e, f, g)); Set(2, 2, Avg3(e, f, g)); Set(1, 3, Avg3(e, f, g));
                Set(3, 2, Avg3(f, g, h)); Set(2, 3, Avg3(f, g, h));
                Set(3, 3, Avg3(g, h, h));
                return;
            }
            case Vp8Modes.BVr:
            {
                int i = Left(0), j = Left(1), k = Left(2), x = Top(-1), a = Top(0), bb = Top(1), c = Top(2), d = Top(3);
                Set(0, 0, Avg2(x, a)); Set(1, 2, Avg2(x, a));
                Set(1, 0, Avg2(a, bb)); Set(2, 2, Avg2(a, bb));
                Set(2, 0, Avg2(bb, c)); Set(3, 2, Avg2(bb, c));
                Set(3, 0, Avg2(c, d));
                Set(0, 3, Avg3(k, j, i));
                Set(0, 2, Avg3(j, i, x));
                Set(0, 1, Avg3(i, x, a)); Set(1, 3, Avg3(i, x, a));
                Set(1, 1, Avg3(x, a, bb)); Set(2, 3, Avg3(x, a, bb));
                Set(2, 1, Avg3(a, bb, c)); Set(3, 3, Avg3(a, bb, c));
                Set(3, 1, Avg3(bb, c, d));
                return;
            }
            case Vp8Modes.BVl:
            {
                int a = Top(0), bb = Top(1), c = Top(2), d = Top(3), e = Top(4), f = Top(5), g = Top(6), h = Top(7);
                Set(0, 0, Avg2(a, bb));
                Set(1, 0, Avg2(bb, c)); Set(0, 2, Avg2(bb, c));
                Set(2, 0, Avg2(c, d)); Set(1, 2, Avg2(c, d));
                Set(3, 0, Avg2(d, e)); Set(2, 2, Avg2(d, e));
                Set(0, 1, Avg3(a, bb, c));
                Set(1, 1, Avg3(bb, c, d)); Set(0, 3, Avg3(bb, c, d));
                Set(2, 1, Avg3(c, d, e)); Set(1, 3, Avg3(c, d, e));
                Set(3, 1, Avg3(d, e, f)); Set(2, 3, Avg3(d, e, f));
                Set(3, 2, Avg3(e, f, g));
                Set(3, 3, Avg3(f, g, h));
                return;
            }
            case Vp8Modes.BHd:
            {
                int i = Left(0), j = Left(1), k = Left(2), l = Left(3), x = Top(-1), a = Top(0), bb = Top(1), c = Top(2);
                Set(0, 0, Avg2(i, x)); Set(2, 1, Avg2(i, x));
                Set(0, 1, Avg2(j, i)); Set(2, 2, Avg2(j, i));
                Set(0, 2, Avg2(k, j)); Set(2, 3, Avg2(k, j));
                Set(0, 3, Avg2(l, k));
                Set(3, 0, Avg3(a, bb, c));
                Set(2, 0, Avg3(x, a, bb));
                Set(1, 0, Avg3(i, x, a)); Set(3, 1, Avg3(i, x, a));
                Set(1, 1, Avg3(j, i, x)); Set(3, 2, Avg3(j, i, x));
                Set(1, 2, Avg3(k, j, i)); Set(3, 3, Avg3(k, j, i));
                Set(1, 3, Avg3(l, k, j));
                return;
            }
            default:
            {
                // Horizontal-up.
                int i = Left(0), j = Left(1), k = Left(2), l = Left(3);
                Set(0, 0, Avg2(i, j));
                Set(2, 0, Avg2(j, k)); Set(0, 1, Avg2(j, k));
                Set(2, 1, Avg2(k, l)); Set(0, 2, Avg2(k, l));
                Set(1, 0, Avg3(i, j, k));
                Set(3, 0, Avg3(j, k, l)); Set(1, 1, Avg3(j, k, l));
                Set(3, 1, Avg3(k, l, l)); Set(1, 2, Avg3(k, l, l));
                Set(3, 2, l); Set(2, 2, l); Set(0, 3, l); Set(1, 3, l); Set(2, 3, l); Set(3, 3, l);
                return;
            }
        }
    }

    private static int Avg3(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;

    private static int Avg2(int a, int b) => (a + b + 1) >> 1;

    private static byte Clip(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
