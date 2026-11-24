using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The same inverse DCT as <see cref="JpegIdct" />, eight lanes at a time: each 256-bit vector is one
///     row of the block, so the column pass is the 1-D transform applied to whole vectors; a transpose
///     turns the rows into columns for the second pass and a second transpose puts the samples back
///     in row order. Single precision, the IJG constants, output scaled by 1/8 and level-shifted.
/// </summary>
internal static class JpegIdctAvx2
{
    public static bool IsSupported => Avx2.IsSupported;

    public static unsafe void Transform(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        fixed (short* coefficients = block)
        fixed (byte* o = output)
        {
            var r0 = Load(coefficients);
            var r1 = Load(coefficients + 8);
            var r2 = Load(coefficients + 16);
            var r3 = Load(coefficients + 24);
            var r4 = Load(coefficients + 32);
            var r5 = Load(coefficients + 40);
            var r6 = Load(coefficients + 48);
            var r7 = Load(coefficients + 56);

            // Columns: lane j of every vector is column j.
            OneDimension(r0, r1, r2, r3, r4, r5, r6, r7, out r0, out r1, out r2, out r3, out r4, out r5, out r6, out r7);
            Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            // Rows: lane k is now row k.
            OneDimension(r0, r1, r2, r3, r4, r5, r6, r7, out r0, out r1, out r2, out r3, out r4, out r5, out r6, out r7);
            Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);

            Store(r0, o);
            Store(r1, o + outputStride);
            Store(r2, o + 2 * outputStride);
            Store(r3, o + 3 * outputStride);
            Store(r4, o + 4 * outputStride);
            Store(r5, o + 5 * outputStride);
            Store(r6, o + 6 * outputStride);
            Store(r7, o + 7 * outputStride);
        }
    }

    /// <summary>
    ///     The transform of a block stored transposed: coefficient (v, u) at [u × 8 + v], as the
    ///     sequential decoder writes it through its transposed zigzag table. Each vector is then one
    ///     column of frequencies, the first pass is the horizontal transform, and after the one transpose
    ///     the second pass hands out rows of samples as they are: one transpose instead of two.
    /// </summary>
    public static unsafe void TransformTransposed(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        fixed (short* coefficients = block)
        fixed (byte* o = output)
        {
            var r0 = Load(coefficients);
            var r1 = Load(coefficients + 8);
            var r2 = Load(coefficients + 16);
            var r3 = Load(coefficients + 24);
            var r4 = Load(coefficients + 32);
            var r5 = Load(coefficients + 40);
            var r6 = Load(coefficients + 48);
            var r7 = Load(coefficients + 56);

            // Vector u holds column u: lane v. The pass over the vectors is the horizontal transform.
            OneDimension(r0, r1, r2, r3, r4, r5, r6, r7, out r0, out r1, out r2, out r3, out r4, out r5, out r6, out r7);
            Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            // Vector v holds vertical frequency v: lane x. The pass over the vectors gives row y in vector y.
            OneDimension(r0, r1, r2, r3, r4, r5, r6, r7, out r0, out r1, out r2, out r3, out r4, out r5, out r6, out r7);

            Store(r0, o);
            Store(r1, o + outputStride);
            Store(r2, o + 2 * outputStride);
            Store(r3, o + 3 * outputStride);
            Store(r4, o + 4 * outputStride);
            Store(r5, o + 5 * outputStride);
            Store(r6, o + 6 * outputStride);
            Store(r7, o + 7 * outputStride);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector256<float> Load(short* row)
        => Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(Sse2.LoadVector128(row)));

    /// <summary>Divides by 8, adds 128, rounds, clamps to 0..255 and writes eight bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Store(Vector256<float> row, byte* destination)
    {
        var scaled = Avx.Add(Avx.Multiply(row, Vector256.Create(0.125f)), Vector256.Create(128.5f));
        var ints = Avx.ConvertToVector256Int32WithTruncation(scaled);
        var shorts = Sse2.PackSignedSaturate(ints.GetLower(), ints.GetUpper());
        var bytes = Sse2.PackUnsignedSaturate(shorts, shorts);
        *(ulong*)destination = bytes.AsUInt64().ToScalar();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OneDimension(
        Vector256<float> s0, Vector256<float> s1, Vector256<float> s2, Vector256<float> s3,
        Vector256<float> s4, Vector256<float> s5, Vector256<float> s6, Vector256<float> s7,
        out Vector256<float> o0, out Vector256<float> o1, out Vector256<float> o2, out Vector256<float> o3,
        out Vector256<float> o4, out Vector256<float> o5, out Vector256<float> o6, out Vector256<float> o7)
    {
        // Even part.
        var p1 = (s2 + s6) * Vector256.Create(0.5411961f);
        var t2 = p1 + s6 * Vector256.Create(-1.847759065f);
        var t3 = p1 + s2 * Vector256.Create(0.765366865f);
        var t0 = s0 + s4;
        var t1 = s0 - s4;
        var x0 = t0 + t3;
        var x3 = t0 - t3;
        var x1 = t1 + t2;
        var x2 = t1 - t2;

        // Odd part.
        t0 = s7;
        t1 = s5;
        t2 = s3;
        t3 = s1;
        var p3 = t0 + t2;
        var p4 = t1 + t3;
        p1 = t0 + t3;
        var p2 = t1 + t2;
        var p5 = (p3 + p4) * Vector256.Create(1.175875602f);
        t0 *= Vector256.Create(0.298631336f);
        t1 *= Vector256.Create(2.053119869f);
        t2 *= Vector256.Create(3.072711026f);
        t3 *= Vector256.Create(1.501321110f);
        p1 = p5 + p1 * Vector256.Create(-0.899976223f);
        p2 = p5 + p2 * Vector256.Create(-2.562915447f);
        p3 *= Vector256.Create(-1.961570560f);
        p4 *= Vector256.Create(-0.390180644f);
        t3 += p1 + p4;
        t2 += p2 + p3;
        t1 += p2 + p4;
        t0 += p1 + p3;

        o0 = x0 + t3;
        o7 = x0 - t3;
        o1 = x1 + t2;
        o6 = x1 - t2;
        o2 = x2 + t1;
        o5 = x2 - t1;
        o3 = x3 + t0;
        o4 = x3 - t0;
    }

    /// <summary>The 8×8 transpose of eight row vectors, in place.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(
        ref Vector256<float> r0, ref Vector256<float> r1, ref Vector256<float> r2, ref Vector256<float> r3,
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

        var u0 = Avx.Shuffle(t0, t2, 0x44);
        var u1 = Avx.Shuffle(t0, t2, 0xEE);
        var u2 = Avx.Shuffle(t1, t3, 0x44);
        var u3 = Avx.Shuffle(t1, t3, 0xEE);
        var u4 = Avx.Shuffle(t4, t6, 0x44);
        var u5 = Avx.Shuffle(t4, t6, 0xEE);
        var u6 = Avx.Shuffle(t5, t7, 0x44);
        var u7 = Avx.Shuffle(t5, t7, 0xEE);

        r0 = Avx.Permute2x128(u0, u4, 0x20);
        r1 = Avx.Permute2x128(u1, u5, 0x20);
        r2 = Avx.Permute2x128(u2, u6, 0x20);
        r3 = Avx.Permute2x128(u3, u7, 0x20);
        r4 = Avx.Permute2x128(u0, u4, 0x31);
        r5 = Avx.Permute2x128(u1, u5, 0x31);
        r6 = Avx.Permute2x128(u2, u6, 0x31);
        r7 = Avx.Permute2x128(u3, u7, 0x31);
    }
}
