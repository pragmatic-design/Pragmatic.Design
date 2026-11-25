using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The forward DCT of Arai, Agui and Nakajima in single precision, eight columns per vector: one
///     pass down the rows, an 8×8 transpose, one pass down what were the columns. Five multiplications
///     and twenty-nine additions per eight-point transform; the scale factors the algorithm leaves out
///     are folded into the quantisation reciprocals, so quantising is one multiply per coefficient.
///     The block leaves <b>transposed</b>: the coefficient of vertical frequency v and horizontal
///     frequency u is at index u·8 + v. That saves the second transpose; the caller reads it accordingly.
/// </summary>
internal static class JpegForwardDctAvx2
{
    public static bool IsSupported => Avx2.IsSupported;

    private const float A = 0.707106781f; // cos(π/4)
    private const float B = 0.382683433f; // cos(3π/8)
    private const float C = 0.541196100f; // √2 · cos(3π/8)
    private const float D = 1.306562965f; // √2 · cos(π/8)

    /// <summary>The AAN output at frequency k is the true coefficient times 8 / (Scale[k]·Scale[l]), both axes.</summary>
    private static ReadOnlySpan<float> Scale =>
    [
        1.0f, 1.387039845f, 1.306562965f, 1.175875602f, 1.0f, 0.785694958f, 0.541196100f, 0.275899379f,
    ];

    /// <summary>
    ///     The reciprocals <see cref="TransformAndQuantize" /> multiplies by, in its transposed layout:
    ///     entry u·8 + v divides the coefficient (v, u) by <paramref name="quantization" />[v·8 + u] and
    ///     undoes the AAN scaling.
    /// </summary>
    public static void BuildReciprocals(ReadOnlySpan<byte> quantization, Span<float> reciprocals)
    {
        var scale = Scale;
        for (var u = 0; u < 8; u++)
        {
            for (var v = 0; v < 8; v++)
                reciprocals[u * 8 + v] = (float)(1.0 / (quantization[v * 8 + u] * (double)scale[u] * scale[v] * 8.0));
        }
    }

    /// <summary>
    ///     Samples at <paramref name="input" /> (row stride <paramref name="stride" />) to 64 quantised
    ///     coefficients, transposed as the class says, via <paramref name="reciprocals" /> from <see cref="BuildReciprocals" />.
    /// </summary>
    public static unsafe void TransformAndQuantize(ReadOnlySpan<byte> input, int stride, ReadOnlySpan<float> reciprocals, Span<short> output)
    {
        fixed (byte* samples = input)
        fixed (float* recip = reciprocals)
        fixed (short* o = output)
        {
            var level = Vector256.Create(128f);
            var r0 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples)) - level;
            var r1 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples + stride)) - level;
            var r2 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples + 2 * stride)) - level;
            var r3 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples + 3 * stride)) - level;
            var r4 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples + 4 * stride)) - level;
            var r5 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples + 5 * stride)) - level;
            var r6 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples + 6 * stride)) - level;
            var r7 = Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(samples + 7 * stride)) - level;

            // Down the rows: lane x of r_v becomes the vertical frequency v of column x.
            Transform8(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            // Now vector x holds column x's eight vertical frequencies; down the vectors is the horizontal transform.
            Transform8(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);

            Quantize(r0, recip, o);
            Quantize(r1, recip + 8, o + 8);
            Quantize(r2, recip + 16, o + 16);
            Quantize(r3, recip + 24, o + 24);
            Quantize(r4, recip + 32, o + 32);
            Quantize(r5, recip + 40, o + 40);
            Quantize(r6, recip + 48, o + 48);
            Quantize(r7, recip + 56, o + 56);
        }
    }

    /// <summary>Eight scaled coefficients to rounded shorts: the conversion rounds to nearest even, as the quantiser should.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Quantize(Vector256<float> coefficients, float* reciprocals, short* output)
    {
        var ints = Avx.ConvertToVector256Int32(coefficients * Avx.LoadVector256(reciprocals));
        Sse2.Store(output, Sse2.PackSignedSaturate(ints.GetLower(), ints.GetUpper()));
    }

    /// <summary>The eight-point AAN transform of d0..d7, in place, output k in dk, scaled as <see cref="Scale" /> says.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform8(ref Vector256<float> d0, ref Vector256<float> d1, ref Vector256<float> d2, ref Vector256<float> d3,
        ref Vector256<float> d4, ref Vector256<float> d5, ref Vector256<float> d6, ref Vector256<float> d7)
    {
        var tmp0 = d0 + d7;
        var tmp7 = d0 - d7;
        var tmp1 = d1 + d6;
        var tmp6 = d1 - d6;
        var tmp2 = d2 + d5;
        var tmp5 = d2 - d5;
        var tmp3 = d3 + d4;
        var tmp4 = d3 - d4;

        // Even part.
        var tmp10 = tmp0 + tmp3;
        var tmp13 = tmp0 - tmp3;
        var tmp11 = tmp1 + tmp2;
        var tmp12 = tmp1 - tmp2;
        d0 = tmp10 + tmp11;
        d4 = tmp10 - tmp11;
        var z1 = (tmp12 + tmp13) * Vector256.Create(A);
        d2 = tmp13 + z1;
        d6 = tmp13 - z1;

        // Odd part.
        tmp10 = tmp4 + tmp5;
        tmp11 = tmp5 + tmp6;
        tmp12 = tmp6 + tmp7;
        var z5 = (tmp10 - tmp12) * Vector256.Create(B);
        var z2 = tmp10 * Vector256.Create(C) + z5;
        var z4 = tmp12 * Vector256.Create(D) + z5;
        var z3 = tmp11 * Vector256.Create(A);
        var z11 = tmp7 + z3;
        var z13 = tmp7 - z3;
        d5 = z13 + z2;
        d3 = z13 - z2;
        d1 = z11 + z4;
        d7 = z11 - z4;
    }

    /// <summary>The 8×8 transpose of eight row vectors: unpack pairs, shuffle quads, then swap 128-bit halves.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(ref Vector256<float> r0, ref Vector256<float> r1, ref Vector256<float> r2, ref Vector256<float> r3,
        ref Vector256<float> r4, ref Vector256<float> r5, ref Vector256<float> r6, ref Vector256<float> r7)
    {
        var t0 = Avx.UnpackLow(r0, r1);
        var t1 = Avx.UnpackHigh(r0, r1);
        var t2 = Avx.UnpackLow(r2, r3);
        var t3 = Avx.UnpackHigh(r2, r3);
        var t4 = Avx.UnpackLow(r4, r5);
        var t5 = Avx.UnpackHigh(r4, r5);
        var t6 = Avx.UnpackLow(r6, r7);
        var t7 = Avx.UnpackHigh(r6, r7);

        var s0 = Avx.Shuffle(t0, t2, 0x44);
        var s1 = Avx.Shuffle(t0, t2, 0xEE);
        var s2 = Avx.Shuffle(t1, t3, 0x44);
        var s3 = Avx.Shuffle(t1, t3, 0xEE);
        var s4 = Avx.Shuffle(t4, t6, 0x44);
        var s5 = Avx.Shuffle(t4, t6, 0xEE);
        var s6 = Avx.Shuffle(t5, t7, 0x44);
        var s7 = Avx.Shuffle(t5, t7, 0xEE);

        r0 = Avx.Permute2x128(s0, s4, 0x20);
        r1 = Avx.Permute2x128(s1, s5, 0x20);
        r2 = Avx.Permute2x128(s2, s6, 0x20);
        r3 = Avx.Permute2x128(s3, s7, 0x20);
        r4 = Avx.Permute2x128(s0, s4, 0x31);
        r5 = Avx.Permute2x128(s1, s5, 0x31);
        r6 = Avx.Permute2x128(s2, s6, 0x31);
        r7 = Avx.Permute2x128(s3, s7, 0x31);
    }

    /// <summary>Bit i set when coefficient i of the block is non-zero: eight compares and a move-mask per row pair.</summary>
    public static unsafe ulong NonZeroMask(ReadOnlySpan<short> block)
    {
        fixed (short* p = block)
        {
            ulong mask = 0;
            var zero = Vector256<short>.Zero;
            for (var row = 0; row < 4; row++)
            {
                // Sixteen coefficients: equal-to-zero gives 0xFFFF per zero lane; the byte mask has two bits per lane.
                var v = Avx.LoadVector256(p + row * 16);
                var isZero = Avx2.CompareEqual(v, zero);
                var bytes = (uint)Avx2.MoveMask(isZero.AsByte());
                // Keep one bit per lane (the low byte of each 16-bit lane), then invert for non-zero.
                var lanes = Bmi2.IsSupported
                    ? Bmi2.ParallelBitExtract(bytes, 0x55555555u)
                    : CompactEvenBits(bytes);
                mask |= (ulong)(~lanes & 0xFFFF) << (row * 16);
            }

            return mask;
        }
    }

    private static uint CompactEvenBits(uint bits)
    {
        uint result = 0;
        for (var i = 0; i < 16; i++)
            result |= ((bits >> (2 * i)) & 1) << i;
        return result;
    }
}
