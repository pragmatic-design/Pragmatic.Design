using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The inverse DCT of a block at reduced size: an N-point transform over the N×N low-frequency
///     coefficients, N = 4, 2 or 1. With the 8-point coefficients kept as they are, the N-point
///     reconstruction is sample = 128 + ΣΣ B[y][r] · B[x][c] · F[r][c], where B[x][u] = C(u) · cos((2x+1)uπ/2N) / 2:
///     at N = 1 that is 128 + F[0][0] / 8, the DC alone. The 4-point case, the thumbnail's, runs on
///     four-lane vectors: 32 fused multiply-adds per block.
/// </summary>
internal static class JpegIdctScaled
{
    private static readonly float[] Basis4 = BuildBasis(4);
    private static readonly float[] Basis2 = BuildBasis(2);

    /// <summary>Column c of the 4-point basis: lane x holds B[x][c].</summary>
    private static readonly float[] Basis4Columns = Transpose(Basis4, 4);

    /// <summary>Dequantised coefficients in natural order to an N×N block of samples.</summary>
    public static void Transform(ReadOnlySpan<short> block, int n, Span<byte> output, int outputStride)
    {
        switch (n)
        {
            case 1:
                output[0] = (byte)Math.Clamp((block[0] + 4 >> 3) + 128, 0, 255);
                return;
            case 2:
                Separable(block, 2, Basis2, output, outputStride);
                return;
            case 4:
                if (Fma.IsSupported && Sse41.IsSupported)
                    Transform4Vector(block, output, outputStride);
                else
                    Separable(block, 4, Basis4, output, outputStride);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(n), n, "A scaled block is 1, 2 or 4 samples on a side.");
        }
    }

    private static unsafe void Transform4Vector(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        fixed (short* f = block)
        fixed (float* columns = Basis4Columns)
        fixed (float* basis = Basis4)
        fixed (byte* o = output)
        {
            var b0 = Vector128.Load(columns);
            var b1 = Vector128.Load(columns + 4);
            var b2 = Vector128.Load(columns + 8);
            var b3 = Vector128.Load(columns + 12);

            // t_r = Σc F[r][c] · column_c, for the four low rows.
            var t0 = Row(f, b0, b1, b2, b3);
            var t1 = Row(f + 8, b0, b1, b2, b3);
            var t2 = Row(f + 16, b0, b1, b2, b3);
            var t3 = Row(f + 24, b0, b1, b2, b3);

            // s_y = Σr B[y][r] · t_r, then + 128, rounded, clamped, four bytes out.
            var offset = Vector128.Create(128.5f);
            for (var y = 0; y < 4; y++)
            {
                var by = basis + y * 4;
                var acc = Vector128.Create(by[0]) * t0;
                acc = Fma.MultiplyAdd(Vector128.Create(by[1]), t1, acc);
                acc = Fma.MultiplyAdd(Vector128.Create(by[2]), t2, acc);
                acc = Fma.MultiplyAdd(Vector128.Create(by[3]), t3, acc);
                var ints = Sse2.ConvertToVector128Int32WithTruncation(Sse.Add(acc, offset));
                var shorts = Sse2.PackSignedSaturate(ints, ints);
                var bytes = Sse2.PackUnsignedSaturate(shorts, shorts);
                *(uint*)(o + y * outputStride) = bytes.AsUInt32().ToScalar();
            }
        }
    }

    private static unsafe Vector128<float> Row(short* coefficients, Vector128<float> b0, Vector128<float> b1, Vector128<float> b2, Vector128<float> b3)
    {
        var acc = Vector128.Create((float)coefficients[0]) * b0;
        acc = Fma.MultiplyAdd(Vector128.Create((float)coefficients[1]), b1, acc);
        acc = Fma.MultiplyAdd(Vector128.Create((float)coefficients[2]), b2, acc);
        acc = Fma.MultiplyAdd(Vector128.Create((float)coefficients[3]), b3, acc);
        return acc;
    }

    private static void Separable(ReadOnlySpan<short> block, int n, float[] basis, Span<byte> output, int outputStride)
    {
        // Rows of coefficients (r = vertical frequency) against the horizontal basis: t[r][x].
        Span<float> temp = stackalloc float[16];
        for (var r = 0; r < n; r++)
        {
            for (var x = 0; x < n; x++)
            {
                var sum = 0f;
                for (var c = 0; c < n; c++)
                    sum += basis[x * n + c] * block[r * 8 + c];
                temp[r * n + x] = sum;
            }
        }

        // Then the vertical basis against t: sample[y][x].
        for (var y = 0; y < n; y++)
        {
            var row = output.Slice(y * outputStride, n);
            for (var x = 0; x < n; x++)
            {
                var sum = 0f;
                for (var r = 0; r < n; r++)
                    sum += basis[y * n + r] * temp[r * n + x];
                row[x] = (byte)Math.Clamp((int)MathF.Round(sum) + 128, 0, 255);
            }
        }
    }

    private static float[] BuildBasis(int n)
    {
        var basis = new float[n * n];
        for (var x = 0; x < n; x++)
        {
            for (var u = 0; u < n; u++)
            {
                var cu = u == 0 ? Math.Sqrt(0.5) : 1.0;
                basis[x * n + u] = (float)(cu * Math.Cos((2 * x + 1) * u * Math.PI / (2 * n)) / 2);
            }
        }

        return basis;
    }

    private static float[] Transpose(float[] basis, int n)
    {
        var transposed = new float[n * n];
        for (var x = 0; x < n; x++)
        {
            for (var u = 0; u < n; u++)
                transposed[u * n + x] = basis[x * n + u];
        }

        return transposed;
    }
}
