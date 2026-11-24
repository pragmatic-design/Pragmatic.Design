namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>The quantisation tables of Annex K.1, scaled for a quality the IJG way.</summary>
internal static class JpegQuantization
{
    private static ReadOnlySpan<byte> LumaBase =>
    [
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    ];

    private static ReadOnlySpan<byte> ChromaBase =>
    [
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    ];

    /// <summary>Tables in natural order, entries 1..255, for <paramref name="quality" /> 1..100.</summary>
    public static (byte[] Luma, byte[] Chroma) ForQuality(int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        var scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        return (Scale(LumaBase, scale), Scale(ChromaBase, scale));
    }

    private static byte[] Scale(ReadOnlySpan<byte> table, int scale)
    {
        var result = new byte[64];
        for (var i = 0; i < 64; i++)
            result[i] = (byte)Math.Clamp((table[i] * scale + 50) / 100, 1, 255);
        return result;
    }
}
