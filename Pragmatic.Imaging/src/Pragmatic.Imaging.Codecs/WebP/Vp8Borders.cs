// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The neighbours a macroblock is predicted from, laid out around it in a work buffer of
///     <see cref="Vp8Prediction.Stride" /> as libwebp lays them out: the row above and column to the left as
///     they were reconstructed, before the loop filter, and outside the frame the row above 127 and the column
///     to the left 129 (RFC 6386, section 12.2). The decoder and the encoder both fill them this way, so the
///     encoder predicts exactly what the decoder will.
/// </summary>
internal static class Vp8Borders
{
    private const int S = Vp8Prediction.Stride;

    /// <summary>Where the macroblock starts in its work buffer: one row of neighbours above, eight bytes to its left.</summary>
    public const int Origin = S + 8;

    /// <summary>The rows a luma work buffer needs: the row above and 16.</summary>
    public const int LumaRows = 17;

    /// <summary>The rows a chroma work buffer needs: the row above and 8.</summary>
    public const int ChromaRows = 9;

    /// <summary>
    ///     Fills the luma neighbours of macroblock (<paramref name="mbX" />, <paramref name="mbY" />). The
    ///     column to the left is the buffer's own last column, which holds the macroblock before it in the row.
    /// </summary>
    /// <param name="y">The luma work buffer.</param>
    /// <param name="top">The bottom row of the macroblock row above, 16 per macroblock.</param>
    /// <param name="mbX">The macroblock's column.</param>
    /// <param name="mbY">The macroblock's row.</param>
    /// <param name="macroblocksWide">How many macroblocks a row has.</param>
    public static void PrepareLuma(byte[] y, ReadOnlySpan<byte> top, int mbX, int mbY, int macroblocksWide)
    {
        var lastColumn = mbX == macroblocksWide - 1;

        // The column to the left, and the corner above it.
        if (mbX == 0)
        {
            for (var j = 0; j < 16; j++)
                y[Origin + j * S - 1] = 129;
            y[Origin - S - 1] = mbY > 0 ? (byte)129 : (byte)127;
        }
        else
        {
            for (var j = 0; j < 16; j++)
                y[Origin + j * S - 1] = y[Origin + j * S + 15];
            y[Origin - S - 1] = mbY > 0 ? top[mbX * 16 - 1] : (byte)127;
        }

        // The row above, and the four pixels to its right.
        if (mbY == 0)
        {
            y.AsSpan(Origin - S, 20).Fill(127);
        }
        else
        {
            top.Slice(mbX * 16, 16).CopyTo(y.AsSpan(Origin - S, 16));
            if (lastColumn)
                y.AsSpan(Origin - S + 16, 4).Fill(top[mbX * 16 + 15]);
            else
                top.Slice(mbX * 16 + 16, 4).CopyTo(y.AsSpan(Origin - S + 16, 4));
        }

        // Subblocks on the right column take their top-right pixels from the macroblock above and to the
        // right, whichever row of subblocks they are in.
        for (var row = 3; row < 15; row += 4)
            y.AsSpan(Origin - S + 16, 4).CopyTo(y.AsSpan(Origin + row * S + 16, 4));
    }

    /// <summary>Fills the neighbours of one chroma plane of the macroblock, the same way.</summary>
    public static void PrepareChroma(byte[] buffer, ReadOnlySpan<byte> top, int mbX, int mbY)
    {
        if (mbX == 0)
        {
            for (var j = 0; j < 8; j++)
                buffer[Origin + j * S - 1] = 129;
            buffer[Origin - S - 1] = mbY > 0 ? (byte)129 : (byte)127;
        }
        else
        {
            for (var j = 0; j < 8; j++)
                buffer[Origin + j * S - 1] = buffer[Origin + j * S + 7];
            buffer[Origin - S - 1] = mbY > 0 ? top[mbX * 8 - 1] : (byte)127;
        }

        if (mbY == 0)
            buffer.AsSpan(Origin - S, 8).Fill(127);
        else
            top.Slice(mbX * 8, 8).CopyTo(buffer.AsSpan(Origin - S, 8));
    }
}
