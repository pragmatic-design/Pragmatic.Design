using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     VP8's inverse transforms (RFC 6386, section 14): the 4 x 4 integer DCT added to a prediction, and the
///     Walsh-Hadamard transform that turns the Y2 block into the DC of the 16 luma blocks. The arithmetic
///     is libwebp's <c>TransformOne</c> and <c>TransformWHT</c>, which match the specification exactly.
/// </summary>
internal static class Vp8InverseTransform
{
    /// <summary>Adds the inverse DCT of <paramref name="input" /> (16 coefficients, raster order) to a 4 x 4 block.</summary>
    public static void AddInverseDct(ReadOnlySpan<short> input, Span<byte> block, int offset, int stride)
    {
        if (Sse2.IsSupported)
            AddInverseDctSse2(input, block, offset, stride);
        else if (Vp8Transpose.IsAccelerated)
            AddInverseDctVector(input, block, offset, stride);
        else
            AddInverseDctScalar(input, block, offset, stride);
    }

    /// <summary>The largest coefficient magnitude for which every intermediate of the 16-bit form stays in 16 bits.</summary>
    private const int Sse2Bound = 2048;

    /// <summary>
    ///     The same arithmetic in 16-bit lanes, as libwebp's <c>ITransform_SSE2</c> does it: <c>x * 85627 >> 16</c>
    ///     is <c>x + (x * 20091 >> 16)</c> and <c>x * 35468 >> 16</c> is <c>x + (x * -30068 >> 16)</c>, each a
    ///     16-bit high multiply. With every coefficient within <see cref="Sse2Bound" /> no sum leaves 16 bits
    ///     (the largest, in the second pass, stays under 30,400); a block with a larger one goes the 32-bit way,
    ///     so the result is the reference arithmetic's for every input.
    /// </summary>
    internal static void AddInverseDctSse2(ReadOnlySpan<short> input, Span<byte> block, int offset, int stride)
    {
        if (input.Length < 16 || block.Length < offset + 3 * stride + 4)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var coefficients = ref MemoryMarshal.GetReference(input);
        var in01 = Vector128.LoadUnsafe(ref coefficients);
        var in23 = Vector128.LoadUnsafe(ref coefficients, 8);
        var bound = Vector128.Create((short)Sse2Bound);
        if (!Vector128.LessThanOrEqualAll((in01 + bound).AsUInt16(), Vector128.Create((ushort)(2 * Sse2Bound))) ||
            !Vector128.LessThanOrEqualAll((in23 + bound).AsUInt16(), Vector128.Create((ushort)(2 * Sse2Bound))))
        {
            AddInverseDctVector(input, block, offset, stride);
            return;
        }

        ref var pixels = ref Unsafe.Add(ref MemoryMarshal.GetReference(block), offset);
        var rows01 = Sse2.UnpackLow(PixelRow(ref pixels, 0), PixelRow(ref pixels, stride)).AsByte();
        var rows23 = Sse2.UnpackLow(PixelRow(ref pixels, 2 * stride), PixelRow(ref pixels, 3 * stride)).AsByte();
        AddAndStore(in01, in23, rows01, rows23, ref pixels, stride);
    }

    /// <summary>
    ///     The same, the prediction given as its four row words (as <see cref="Vp8Prediction.Subblocks" /> gives
    ///     them): the block is written with prediction plus residue, without the prediction being first written
    ///     there and read back.
    /// </summary>
    internal static void AddInverseDctSse2(ReadOnlySpan<short> input, ReadOnlySpan<uint> prediction, Span<byte> block, int offset, int stride)
    {
        if (input.Length < 16 || prediction.Length < 4 || block.Length < offset + 3 * stride + 4)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var coefficients = ref MemoryMarshal.GetReference(input);
        var in01 = Vector128.LoadUnsafe(ref coefficients);
        var in23 = Vector128.LoadUnsafe(ref coefficients, 8);
        ref var pixels = ref Unsafe.Add(ref MemoryMarshal.GetReference(block), offset);
        ref var rows = ref MemoryMarshal.GetReference(prediction);
        var bound = Vector128.Create((short)Sse2Bound);
        if (!Vector128.LessThanOrEqualAll((in01 + bound).AsUInt16(), Vector128.Create((ushort)(2 * Sse2Bound))) ||
            !Vector128.LessThanOrEqualAll((in23 + bound).AsUInt16(), Vector128.Create((ushort)(2 * Sse2Bound))))
        {
            Unsafe.WriteUnaligned(ref pixels, rows);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, stride), Unsafe.Add(ref rows, 1));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, 2 * stride), Unsafe.Add(ref rows, 2));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, 3 * stride), Unsafe.Add(ref rows, 3));
            AddInverseDctVector(input, block, offset, stride);
            return;
        }

        var rows01 = Sse2.UnpackLow(Vector128.CreateScalar(rows).AsInt32(), Vector128.CreateScalar(Unsafe.Add(ref rows, 1)).AsInt32()).AsByte();
        var rows23 = Sse2.UnpackLow(Vector128.CreateScalar(Unsafe.Add(ref rows, 2)).AsInt32(), Vector128.CreateScalar(Unsafe.Add(ref rows, 3)).AsInt32()).AsByte();
        AddAndStore(in01, in23, rows01, rows23, ref pixels, stride);
    }

    /// <summary>The two passes over coefficients within the bound, added to the prediction rows (two to a vector) and stored.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddAndStore(Vector128<short> in01, Vector128<short> in23, Vector128<byte> rows01, Vector128<byte> rows23, ref byte pixels, int stride)
    {
        var (v01, v23) = Pass(in01, in23, Vector128<short>.Zero);
        var (t01, t23) = Transpose(v01, v23);
        var (h01, h23) = Pass(t01, t23, Vector128.Create((short)4, 4, 4, 4, 0, 0, 0, 0));
        var (residue01, residue23) = Transpose(Sse2.ShiftRightArithmetic(h01, 3), Sse2.ShiftRightArithmetic(h23, 3));

        var zero = Vector128<byte>.Zero;
        var sum01 = Sse2.UnpackLow(rows01, zero).AsInt16() + residue01;
        var sum23 = Sse2.UnpackLow(rows23, zero).AsInt16() + residue23;
        var bytes = Sse2.PackUnsignedSaturate(sum01, sum23).AsUInt32();
        Unsafe.WriteUnaligned(ref pixels, bytes.GetElement(0));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, stride), bytes.GetElement(1));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, 2 * stride), bytes.GetElement(2));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, 3 * stride), bytes.GetElement(3));
    }

    /// <summary>
    ///     One pass of the 16-bit form over the four columns (rows 0 and 1 in <paramref name="in01" />, 2 and 3
    ///     in <paramref name="in23" />), <paramref name="rounding" /> added to the DC lanes: libwebp's arrangement,
    ///     line for line. The results come out as rows 0 and 1, then 2 and 3.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<short> T01, Vector128<short> T23) Pass(Vector128<short> in01, Vector128<short> in23, Vector128<short> rounding)
    {
        var k1k2 = Vector128.Create((short)20091, 20091, 20091, 20091, -30068, -30068, -30068, -30068);
        var k2k1 = Vector128.Create((short)-30068, -30068, -30068, -30068, 20091, 20091, 20091, 20091);
        var in1 = Sse2.UnpackHigh(in01.AsInt64(), in01.AsInt64()).AsInt16();
        var in3 = Sse2.UnpackHigh(in23.AsInt64(), in23.AsInt64()).AsInt16();
        var dc = in01 + rounding;

        // c = MUL(in1, K2) - MUL(in3, K1) = MUL(in1, k2) - MUL(in3, k1) + in1 - in3, and d alike.
        var ad3 = dc + in23;
        var bc3 = dc - in23;
        var c1d1 = Sse2.MultiplyHigh(in1, k2k1);
        var c2d2 = Sse2.MultiplyHigh(in3, k1k2);
        var c = Sse2.UnpackHigh(bc3.AsInt64(), bc3.AsInt64()).AsInt16() + (c1d1 - c2d2);
        var du = ad3 + (c1d1 + c2d2);
        var d = Sse2.UnpackHigh(du.AsInt64(), du.AsInt64()).AsInt16();

        var ab = Sse2.UnpackLow(ad3.AsInt64(), bc3.AsInt64()).AsInt16();
        var dcPair = Sse2.UnpackLow(d.AsInt64(), c.AsInt64()).AsInt16();
        return (ab + dcPair, Sse2.Shuffle((ab - dcPair).AsInt32(), 0x4E).AsInt16());
    }

    /// <summary>The 4 x 4 transpose of two vectors of two rows each, by two rounds of 16-bit unpacks.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<short> T01, Vector128<short> T23) Transpose(Vector128<short> rows01, Vector128<short> rows23)
    {
        var x0 = Sse2.UnpackLow(rows01, rows23);
        var x1 = Sse2.UnpackHigh(rows01, rows23);
        return (Sse2.UnpackLow(x0, x1), Sse2.UnpackHigh(x0, x1));
    }

    /// <summary>A row's four pixels, in the low lanes.</summary>
    private static Vector128<int> PixelRow(ref byte block, int at) =>
        Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, at))).AsInt32();

    /// <summary>
    ///     The same arithmetic four lanes at a time: the first pass combines the coefficient rows as loaded,
    ///     and the block is transposed between the passes and before it is added.
    /// </summary>
    internal static void AddInverseDctVector(ReadOnlySpan<short> input, Span<byte> block, int offset, int stride)
    {
        if (input.Length < 16 || block.Length < offset + 3 * stride + 4)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var coefficients = ref MemoryMarshal.GetReference(input);
        var low = Vector128.LoadUnsafe(ref coefficients);
        var high = Vector128.LoadUnsafe(ref coefficients, 8);
        var i0 = Vector128.WidenLower(low);
        var i1 = Vector128.WidenUpper(low);
        var i2 = Vector128.WidenLower(high);
        var i3 = Vector128.WidenUpper(high);

        // Vertical pass, each lane one column.
        var a = i0 + i2;
        var b = i0 - i2;
        var c = Mul2(i1) - Mul1(i3);
        var d = Mul1(i1) + Mul2(i3);
        var p0 = a + d;
        var p1 = b + c;
        var p2 = b - c;
        var p3 = a - d;

        // Horizontal pass, each lane one row.
        Vp8Transpose.Apply(ref p0, ref p1, ref p2, ref p3);
        var dc = p0 + Vector128.Create(4);
        a = dc + p2;
        b = dc - p2;
        c = Mul2(p1) - Mul1(p3);
        d = Mul1(p1) + Mul2(p3);
        var e0 = Vector128.ShiftRightArithmetic(a + d, 3);
        var e1 = Vector128.ShiftRightArithmetic(b + c, 3);
        var e2 = Vector128.ShiftRightArithmetic(b - c, 3);
        var e3 = Vector128.ShiftRightArithmetic(a - d, 3);
        Vp8Transpose.Apply(ref e0, ref e1, ref e2, ref e3);

        ref var pixels = ref Unsafe.Add(ref MemoryMarshal.GetReference(block), offset);
        var row0 = Add(ref pixels, e0);
        var row1 = Add(ref Unsafe.Add(ref pixels, stride), e1);
        var row2 = Add(ref Unsafe.Add(ref pixels, 2 * stride), e2);
        var row3 = Add(ref Unsafe.Add(ref pixels, 3 * stride), e3);
        var bytes = Vector128.Narrow(Vector128.Narrow(row0, row1), Vector128.Narrow(row2, row3)).AsUInt32();
        Unsafe.WriteUnaligned(ref pixels, bytes.GetElement(0));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, stride), bytes.GetElement(1));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, 2 * stride), bytes.GetElement(2));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref pixels, 3 * stride), bytes.GetElement(3));
    }

    /// <summary>A row of four pixels plus four residues, clamped to a byte, still in 32-bit lanes.</summary>
    private static Vector128<int> Add(ref byte row, Vector128<int> residue)
    {
        var pixels = Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref row)).AsByte();
        var sum = Vector128.WidenLower(Vector128.WidenLower(pixels)).AsInt32() + residue;
        return Vector128.Min(Vector128.Max(sum, Vector128<int>.Zero), Vector128.Create(255));
    }

    private static Vector128<int> Mul1(Vector128<int> a) => Vector128.ShiftRightArithmetic(a * 20091, 16) + a;

    private static Vector128<int> Mul2(Vector128<int> a) => Vector128.ShiftRightArithmetic(a * 35468, 16);

    [SkipLocalsInit]
    internal static void AddInverseDctScalar(ReadOnlySpan<short> input, Span<byte> block, int offset, int stride)
    {
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            var a = input[i] + input[8 + i];
            var b = input[i] - input[8 + i];
            var c = Mul2(input[4 + i]) - Mul1(input[12 + i]);
            var d = Mul1(input[4 + i]) + Mul2(input[12 + i]);
            temp[i * 4] = a + d;
            temp[i * 4 + 1] = b + c;
            temp[i * 4 + 2] = b - c;
            temp[i * 4 + 3] = a - d;
        }

        for (var i = 0; i < 4; i++)
        {
            var dc = temp[i] + 4;
            var a = dc + temp[8 + i];
            var b = dc - temp[8 + i];
            var c = Mul2(temp[4 + i]) - Mul1(temp[12 + i]);
            var d = Mul1(temp[4 + i]) + Mul2(temp[12 + i]);
            var row = offset + i * stride;
            Store(block, row, a + d);
            Store(block, row + 1, b + c);
            Store(block, row + 2, b - c);
            Store(block, row + 3, a - d);
        }
    }

    /// <summary>The inverse Walsh-Hadamard transform of the Y2 block, written to the DC of each of the 16 luma blocks.</summary>
    [SkipLocalsInit]
    public static void Walsh(ReadOnlySpan<short> input, Span<short> coefficients)
    {
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            var a0 = input[i] + input[12 + i];
            var a1 = input[4 + i] + input[8 + i];
            var a2 = input[4 + i] - input[8 + i];
            var a3 = input[i] - input[12 + i];
            temp[i] = a0 + a1;
            temp[8 + i] = a0 - a1;
            temp[4 + i] = a3 + a2;
            temp[12 + i] = a3 - a2;
        }

        for (var i = 0; i < 4; i++)
        {
            var dc = temp[i * 4] + 3;
            var a0 = dc + temp[i * 4 + 3];
            var a1 = temp[i * 4 + 1] + temp[i * 4 + 2];
            var a2 = temp[i * 4 + 1] - temp[i * 4 + 2];
            var a3 = dc - temp[i * 4 + 3];
            var block = i * 4 * 16;
            coefficients[block] = (short)((a0 + a1) >> 3);
            coefficients[block + 16] = (short)((a3 + a2) >> 3);
            coefficients[block + 32] = (short)((a0 - a1) >> 3);
            coefficients[block + 48] = (short)((a3 - a2) >> 3);
        }
    }

    private static int Mul1(int a) => ((a * 20091) >> 16) + a;

    private static int Mul2(int a) => (a * 35468) >> 16;

    private static void Store(Span<byte> block, int index, int value)
    {
        var sum = block[index] + (value >> 3);
        block[index] = (byte)(sum < 0 ? 0 : sum > 255 ? 255 : sum);
    }
}
