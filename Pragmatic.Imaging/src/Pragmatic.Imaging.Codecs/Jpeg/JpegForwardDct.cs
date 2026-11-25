namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The forward DCT of one 8×8 block of samples, as two matrix products with the cosine basis, in
///     single precision: F = C · (S − 128) · Cᵀ. Straightforward and exact; a factorised form is a later
///     optimisation.
/// </summary>
internal static class JpegForwardDct
{
    private static readonly float[] Basis = BuildBasis();

    /// <summary>Samples in <paramref name="input" /> (row stride <paramref name="stride" />) to 64 coefficients in natural order.</summary>
    public static void Transform(ReadOnlySpan<byte> input, int stride, Span<float> output)
    {
        Span<float> shifted = stackalloc float[64];
        for (var y = 0; y < 8; y++)
        {
            var row = input.Slice(y * stride, 8);
            for (var x = 0; x < 8; x++)
                shifted[y * 8 + x] = row[x] - 128f;
        }

        // Rows: T = (S − 128) · Cᵀ, so T[y, v] = Σx S[y, x] · C[v, x].
        Span<float> temp = stackalloc float[64];
        for (var y = 0; y < 8; y++)
        {
            for (var v = 0; v < 8; v++)
            {
                var sum = 0f;
                for (var x = 0; x < 8; x++)
                    sum += shifted[y * 8 + x] * Basis[v * 8 + x];
                temp[y * 8 + v] = sum;
            }
        }

        // Columns: F = C · T, so F[u, v] = Σy C[u, y] · T[y, v].
        for (var u = 0; u < 8; u++)
        {
            for (var v = 0; v < 8; v++)
            {
                var sum = 0f;
                for (var y = 0; y < 8; y++)
                    sum += Basis[u * 8 + y] * temp[y * 8 + v];
                output[u * 8 + v] = sum;
            }
        }
    }

    private static float[] BuildBasis()
    {
        var basis = new float[64];
        for (var u = 0; u < 8; u++)
        {
            var cu = u == 0 ? Math.Sqrt(0.5) : 1.0;
            for (var x = 0; x < 8; x++)
                basis[u * 8 + x] = (float)(0.5 * cu * Math.Cos((2 * x + 1) * u * Math.PI / 16));
        }

        return basis;
    }
}
