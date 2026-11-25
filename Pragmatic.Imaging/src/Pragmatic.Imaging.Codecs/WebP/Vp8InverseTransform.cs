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
