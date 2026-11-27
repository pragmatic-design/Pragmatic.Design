namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What the encoder chose for one macroblock: its prediction modes and the quantized levels of its
///     blocks (scan order, 16 per block), with the rate-distortion account that chose them. One instance is
///     reused macroblock after macroblock.
/// </summary>
internal sealed class Vp8MacroblockDecision
{
    /// <summary>Whether the luma is predicted whole, with its DC in a Y2 block.</summary>
    public bool IsLuma16 { get; set; }

    public int Luma16Mode { get; set; }

    /// <summary>The 16 subblock modes, raster order, when the luma is predicted in 4 x 4 blocks.</summary>
    public int[] SubblockModes { get; } = new int[16];

    public int ChromaMode { get; set; }

    /// <summary>The Y2 block's levels.</summary>
    public short[] Y2Levels { get; } = new short[16];

    /// <summary>The 16 luma blocks' levels; position 0 of each is zero when there is a Y2 block.</summary>
    public short[] LumaLevels { get; } = new short[16 * 16];

    /// <summary>The 4 U blocks' levels, then the 4 V blocks'.</summary>
    public short[] ChromaLevels { get; } = new short[8 * 16];

    /// <summary>The distortion, its texture part, the rate of the levels and of the modes, and their weighted score.</summary>
    public Vp8Score Score;
}
