using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     Unfiltering Sub, Average and Paeth for 3- and 4-byte pixels, the libpng way: one pixel per step,
///     its channels side by side in vector lanes, the dependency on the pixel to the left carried in a
///     register. A 4-byte pixel is loaded and stored as one 32-bit word; a 3-byte one is read as four
///     bytes (one past, which is the next pixel's first byte) and written back as three.
/// </summary>
internal static unsafe class PngUnfilterSimd
{
    public static bool IsSupported => Sse2.IsSupported;

    public static void Sub(Span<byte> row, int bpp)
    {
        fixed (byte* p = row)
        {
            var previous = Vector128<byte>.Zero;
            var end = p + row.Length;
            if (bpp == 4)
            {
                for (var q = p; q + 4 <= end; q += 4)
                {
                    var current = Sse2.Add(Load4(q), previous);
                    Store4(q, current);
                    previous = current;
                }

                return;
            }

            for (var q = p; q + 3 <= end; q += 3)
            {
                var current = Sse2.Add(Load3(q, end), previous);
                Store3(q, current);
                previous = current;
            }
        }
    }

    public static void Average(Span<byte> row, ReadOnlySpan<byte> prior, int bpp)
    {
        fixed (byte* p = row)
        fixed (byte* up = prior)
        {
            var previous = Vector128<byte>.Zero;
            var end = p + row.Length;
            var one = Vector128.Create((byte)1);
            var offset = 0;
            if (bpp == 4)
            {
                for (; offset + 4 <= row.Length; offset += 4)
                {
                    var b = Load4(up + offset);
                    // pavgb rounds up; PNG wants the floor: subtract one where exactly one of the two is odd.
                    var mean = Sse2.Subtract(Sse2.Average(previous, b), Sse2.And(Sse2.Xor(previous, b), one));
                    var current = Sse2.Add(Load4(p + offset), mean);
                    Store4(p + offset, current);
                    previous = current;
                }

                return;
            }

            for (; offset + 3 <= row.Length; offset += 3)
            {
                var b = Load3(up + offset, up + prior.Length);
                var mean = Sse2.Subtract(Sse2.Average(previous, b), Sse2.And(Sse2.Xor(previous, b), one));
                var current = Sse2.Add(Load3(p + offset, end), mean);
                Store3(p + offset, current);
                previous = current;
            }
        }
    }

    public static void Paeth(Span<byte> row, ReadOnlySpan<byte> prior, int bpp)
    {
        fixed (byte* p = row)
        fixed (byte* up = prior)
        {
            // Widened to 16-bit lanes: a (left), c (up-left) carried from the previous pixel.
            var a = Vector128<short>.Zero;
            var c = Vector128<short>.Zero;
            var end = p + row.Length;
            var offset = 0;
            if (bpp == 4)
            {
                for (; offset + 4 <= row.Length; offset += 4)
                {
                    var b = Widen(Load4(up + offset));
                    var predicted = Predict(a, b, c);
                    var current = Sse2.Add(Load4(p + offset), Narrow(predicted));
                    Store4(p + offset, current);
                    c = b;
                    a = Widen(current);
                }

                return;
            }

            for (; offset + 3 <= row.Length; offset += 3)
            {
                var b = Widen(Load3(up + offset, up + prior.Length));
                var predicted = Predict(a, b, c);
                var current = Sse2.Add(Load3(p + offset, end), Narrow(predicted));
                Store3(p + offset, current);
                c = b;
                a = Widen(current);
            }
        }
    }

    /// <summary>The Paeth predictor on 16-bit lanes: pa = |b − c|, pb = |a − c|, pc = |a + b − 2c|; ties go a, then b.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Predict(Vector128<short> a, Vector128<short> b, Vector128<short> c)
    {
        var pa = Abs(Sse2.Subtract(b, c));
        var pb = Abs(Sse2.Subtract(a, c));
        var pc = Abs(Sse2.Subtract(Sse2.Add(a, b), Sse2.Add(c, c)));
        // a when pa <= pb and pa <= pc: that is, not (pb < pa) and not (pc < pa).
        var notA = Sse2.Or(Sse2.CompareGreaterThan(pa, pb), Sse2.CompareGreaterThan(pa, pc));
        var useB = Sse2.AndNot(Sse2.CompareGreaterThan(pb, pc), notA); // pb <= pc, and not a
        var useC = Sse2.And(Sse2.CompareGreaterThan(pb, pc), notA);
        return Sse2.Or(Sse2.Or(Sse2.AndNot(notA, a), Sse2.And(useB, b)), Sse2.And(useC, c));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Abs(Vector128<short> v)
    {
        if (Ssse3.IsSupported)
            return Ssse3.Abs(v).AsInt16();
        var sign = Sse2.ShiftRightArithmetic(v, 15);
        return Sse2.Subtract(Sse2.Xor(v, sign), sign);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Widen(Vector128<byte> v) => Sse2.UnpackLow(v, Vector128<byte>.Zero).AsInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Narrow(Vector128<short> v) => Sse2.PackUnsignedSaturate(v, v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Load4(byte* p) => Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(p)).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store4(byte* p, Vector128<byte> v) => Unsafe.WriteUnaligned(p, v.AsUInt32().ToScalar());

    /// <summary>Three bytes, as four when a fourth exists past them, else padded.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Load3(byte* p, byte* end)
    {
        var value = p + 4 <= end
            ? Unsafe.ReadUnaligned<uint>(p)
            : (uint)(p[0] | (p[1] << 8) | (p[2] << 16));
        return Vector128.CreateScalar(value & 0x00FFFFFFu).AsByte();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store3(byte* p, Vector128<byte> v)
    {
        var value = v.AsUInt32().ToScalar();
        Unsafe.WriteUnaligned(p, (ushort)value);
        p[2] = (byte)(value >> 16);
    }
}
