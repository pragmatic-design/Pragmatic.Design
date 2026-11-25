namespace Pragmatic.Imaging.Codecs.Barcodes;

/// <summary>Reed-Solomon over GF(2^8) with the QR polynomial x^8 + x^4 + x^3 + x^2 + 1.</summary>
internal static class ReedSolomon
{
    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static ReedSolomon()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if (x >= 256)
                x ^= 0x11D;
        }

        for (var i = 255; i < 512; i++)
            Exp[i] = Exp[i - 255];
    }

    public static byte Multiply(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    /// <summary>The generator polynomial for <paramref name="degree" /> error correction codewords: (x − α^0)(x − α^1)…</summary>
    public static byte[] Generator(int degree)
    {
        var result = new byte[degree];
        result[^1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], (byte)root);
                if (j + 1 < degree)
                    result[j] ^= result[j + 1];
            }

            root = Multiply((byte)root, 2);
        }

        return result;
    }

    /// <summary>The remainder of the data polynomial divided by the generator: the error correction codewords.</summary>
    public static byte[] Remainder(byte[] data, byte[] generator)
    {
        var result = new byte[generator.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
                result[i] ^= Multiply(generator[i], factor);
        }

        return result;
    }
}
