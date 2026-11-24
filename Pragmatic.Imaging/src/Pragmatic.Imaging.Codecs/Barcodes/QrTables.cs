namespace Pragmatic.Imaging.Codecs.Barcodes;

/// <summary>The tables of ISO/IEC 18004 a QR encoder needs at error correction level M; the arithmetic is in <see cref="ReedSolomon" />.</summary>
internal static class QrTables
{
    /// <summary>
    ///     Per version at level M: error correction codewords per block, blocks in group 1 and their data
    ///     codewords, blocks in group 2 and theirs (Table 9 of the standard).
    /// </summary>
    private static readonly (int Ec, int Blocks1, int Data1, int Blocks2, int Data2)[] BlocksM =
    [
        (10, 1, 16, 0, 0), (16, 1, 28, 0, 0), (26, 1, 44, 0, 0), (18, 2, 32, 0, 0), (24, 2, 43, 0, 0),
        (16, 4, 27, 0, 0), (18, 4, 31, 0, 0), (22, 2, 38, 2, 39), (22, 3, 36, 2, 37), (26, 4, 43, 1, 44),
        (30, 1, 50, 4, 51), (22, 6, 36, 2, 37), (22, 8, 37, 1, 38), (24, 4, 40, 5, 41), (24, 5, 41, 5, 42),
        (28, 7, 45, 3, 46), (28, 10, 46, 1, 47), (26, 9, 43, 4, 44), (26, 3, 44, 11, 45), (26, 3, 41, 13, 42),
        (26, 17, 42, 0, 0), (28, 17, 46, 0, 0), (28, 4, 47, 14, 48), (28, 6, 45, 14, 46), (28, 8, 47, 13, 48),
        (28, 19, 46, 4, 47), (28, 22, 45, 3, 46), (28, 3, 45, 23, 46), (28, 21, 45, 7, 46), (28, 19, 47, 10, 48),
        (28, 2, 46, 29, 47), (28, 10, 46, 23, 47), (28, 14, 46, 21, 47), (28, 14, 46, 23, 47), (28, 12, 47, 26, 48),
        (28, 6, 47, 34, 48), (28, 29, 46, 14, 47), (28, 13, 46, 32, 47), (28, 40, 47, 7, 48), (28, 18, 47, 31, 48),
    ];

    public static (int Ec, int Blocks1, int Data1, int Blocks2, int Data2) Blocks(int version) => BlocksM[version - 1];

    public static int DataCodewords(int version)
    {
        var (_, blocks1, data1, blocks2, data2) = BlocksM[version - 1];
        return blocks1 * data1 + blocks2 * data2;
    }

    /// <summary>The codewords a version holds in all, from the modules left after the function patterns: checks the table.</summary>
    public static int TotalCodewords(int version)
    {
        var size = 17 + 4 * version;
        var modules = size * size;
        modules -= 3 * 64; // finders with separators (8×8 each)
        modules -= 2 * (size - 16); // timing patterns
        modules -= 31; // format information, including the dark module
        var alignment = AlignmentPositions(version).Length;
        if (alignment > 0)
            modules -= (alignment * alignment - 3) * 25 - 2 * (alignment - 2) * 5; // patterns; where they cross the timing lines was counted already
        if (version >= 7)
            modules -= 36; // version information
        return modules / 8;
    }

    /// <summary>The centre coordinates of the alignment patterns, on both axes.</summary>
    public static int[] AlignmentPositions(int version)
    {
        if (version == 1)
            return [];
        var count = version / 7 + 2;
        var step = version == 32 ? 26 : (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
        var positions = new int[count];
        positions[0] = 6;
        var position = 17 + 4 * version - 7;
        for (var i = count - 1; i >= 1; i--, position -= step)
            positions[i] = position;
        return positions;
    }
}
