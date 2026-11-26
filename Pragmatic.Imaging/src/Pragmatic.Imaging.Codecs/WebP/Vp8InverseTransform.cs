using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
        if (Vp8Transpose.IsAccelerated)
            AddInverseDctVector(input, block, offset, stride);
        else
            AddInverseDctScalar(input, block, offset, stride);
    }

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
