namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>The marker codes a decoder meets, the byte after 0xFF.</summary>
internal static class JpegMarker
{
    public const byte Sof0 = 0xC0; // baseline DCT
    public const byte Sof1 = 0xC1; // extended sequential DCT, Huffman
    public const byte Sof2 = 0xC2; // progressive DCT, Huffman
    public const byte Dht = 0xC4;
    public const byte Rst0 = 0xD0;
    public const byte Rst7 = 0xD7;
    public const byte Soi = 0xD8;
    public const byte Eoi = 0xD9;
    public const byte Sos = 0xDA;
    public const byte Dqt = 0xDB;
    public const byte Dri = 0xDD;
    public const byte App0 = 0xE0;
    public const byte App1 = 0xE1;
    public const byte App15 = 0xEF;
    public const byte Com = 0xFE;

    /// <summary>The natural (row-major) index of the k-th coefficient in zigzag order.</summary>
    public static ReadOnlySpan<byte> Zigzag =>
    [
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];
}
