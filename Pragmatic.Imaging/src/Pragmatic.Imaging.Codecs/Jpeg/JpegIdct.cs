namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The inverse DCT of one 8×8 block, in integers: the separable algorithm of the IJG's accurate
///     path (constants scaled by 2^12, two extra bits of precision between the passes), as also found in
///     stb_image. Takes dequantised coefficients in natural order; writes 8×8 samples, 0..255.
/// </summary>
internal static class JpegIdct
{
    private const int F0_298 = 1223; // 0.298631336 * 4096
    private const int F0_390 = 1598; // 0.390180644
    private const int F0_541 = 2217; // 0.5411961
    private const int F0_765 = 3135; // 0.765366865
    private const int F0_899 = 3686; // 0.899976223
    private const int F1_175 = 4816; // 1.175875602
    private const int F1_501 = 6149; // 1.501321110
    private const int F1_847 = 7568; // 1.847759065
    private const int F1_961 = 8035; // 1.961570560
    private const int F2_053 = 8410; // 2.053119869
    private const int F2_562 = 10498; // 2.562915447
    private const int F3_072 = 12586; // 3.072711026

    /// <summary>The vector transform where the hardware has AVX2, the scalar one elsewhere.</summary>
    public static void Transform(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        if (JpegIdctAvx2.IsSupported)
            JpegIdctAvx2.Transform(block, output, outputStride);
        else
            TransformScalar(block, output, outputStride);
    }

    /// <summary>
    ///     A block stored transposed, coefficient (v, u) at [u × 8 + v]: the vector transform takes it as
    ///     it is and saves a transpose (see <see cref="JpegIdctAvx2.TransformTransposed" />); the scalar
    ///     one puts it back in natural order first.
    /// </summary>
    public static void TransformTransposed(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        if (JpegIdctAvx2.IsSupported)
        {
            JpegIdctAvx2.TransformTransposed(block, output, outputStride);
            return;
        }

        Span<short> natural = stackalloc short[64];
        ToNatural(block, natural);
        TransformScalar(natural, output, outputStride);
    }

    private static void ToNatural(ReadOnlySpan<short> transposed, Span<short> natural)
    {
        for (var v = 0; v < 8; v++)
        {
            for (var u = 0; u < 8; u++)
                natural[v * 8 + u] = transposed[u * 8 + v];
        }
    }

    public static void TransformScalar(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        Span<int> work = stackalloc int[64];

        // Columns.
        for (var i = 0; i < 8; i++)
        {
            if (block[i + 8] == 0 && block[i + 16] == 0 && block[i + 24] == 0 && block[i + 32] == 0
                && block[i + 40] == 0 && block[i + 48] == 0 && block[i + 56] == 0)
            {
                var dc = block[i] * 4;
                work[i] = dc;
                work[i + 8] = dc;
                work[i + 16] = dc;
                work[i + 24] = dc;
                work[i + 32] = dc;
                work[i + 40] = dc;
                work[i + 48] = dc;
                work[i + 56] = dc;
                continue;
            }

            OneDimension(block[i], block[i + 8], block[i + 16], block[i + 24], block[i + 32], block[i + 40], block[i + 48], block[i + 56],
                out var x0, out var x1, out var x2, out var x3, out var t0, out var t1, out var t2, out var t3);

            // Constants scaled by 2^12; bring back down, keeping 2 extra bits of precision.
            x0 += 512;
            x1 += 512;
            x2 += 512;
            x3 += 512;
            work[i] = (x0 + t3) >> 10;
            work[i + 56] = (x0 - t3) >> 10;
            work[i + 8] = (x1 + t2) >> 10;
            work[i + 48] = (x1 - t2) >> 10;
            work[i + 16] = (x2 + t1) >> 10;
            work[i + 40] = (x2 - t1) >> 10;
            work[i + 24] = (x3 + t0) >> 10;
            work[i + 32] = (x3 - t0) >> 10;
        }

        // Rows.
        for (var i = 0; i < 8; i++)
        {
            var row = work.Slice(i * 8, 8);
            OneDimension(row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7],
                out var x0, out var x1, out var x2, out var x3, out var t0, out var t1, out var t2, out var t3);

            // 2^12 from the constants, 2^2 from the first pass, 2^3 from the two sqrt(8): 2^17 to remove,
            // rounded (+65536), and the level shift of 128 added before the shift.
            const int bias = 65536 + (128 << 17);
            x0 += bias;
            x1 += bias;
            x2 += bias;
            x3 += bias;
            var o = output.Slice(i * outputStride, 8);
            o[0] = Clamp((x0 + t3) >> 17);
            o[7] = Clamp((x0 - t3) >> 17);
            o[1] = Clamp((x1 + t2) >> 17);
            o[6] = Clamp((x1 - t2) >> 17);
            o[2] = Clamp((x2 + t1) >> 17);
            o[5] = Clamp((x2 - t1) >> 17);
            o[3] = Clamp((x3 + t0) >> 17);
            o[4] = Clamp((x3 - t0) >> 17);
        }
    }

    private static void OneDimension(int s0, int s1, int s2, int s3, int s4, int s5, int s6, int s7,
        out int x0, out int x1, out int x2, out int x3, out int t0, out int t1, out int t2, out int t3)
    {
        // Even part.
        var p2 = s2;
        var p3 = s6;
        var p1 = (p2 + p3) * F0_541;
        t2 = p1 + p3 * -F1_847;
        t3 = p1 + p2 * F0_765;
        p2 = s0;
        p3 = s4;
        t0 = (p2 + p3) * 4096;
        t1 = (p2 - p3) * 4096;
        x0 = t0 + t3;
        x3 = t0 - t3;
        x1 = t1 + t2;
        x2 = t1 - t2;

        // Odd part.
        t0 = s7;
        t1 = s5;
        t2 = s3;
        t3 = s1;
        p3 = t0 + t2;
        var p4 = t1 + t3;
        p1 = t0 + t3;
        p2 = t1 + t2;
        var p5 = (p3 + p4) * F1_175;
        t0 *= F0_298;
        t1 *= F2_053;
        t2 *= F3_072;
        t3 *= F1_501;
        p1 = p5 + p1 * -F0_899;
        p2 = p5 + p2 * -F2_562;
        p3 *= -F1_961;
        p4 *= -F0_390;
        t3 += p1 + p4;
        t2 += p2 + p3;
        t1 += p2 + p4;
        t0 += p1 + p3;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
