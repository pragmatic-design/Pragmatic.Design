namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The first-partition data of one macroblock of a key frame (RFC 6386, sections 10 and 11): its segment,
///     whether it skips its coefficients, and its intra modes, read the way libwebp's <c>ParseIntraMode</c>
///     reads them.
/// </summary>
internal sealed class Vp8MacroblockModes
{
    public int Segment { get; private set; }

    public bool Skip { get; private set; }

    /// <summary>Whether the luma is predicted in 16 4 x 4 blocks, each with its own mode, rather than whole.</summary>
    public bool IsSubblocks { get; private set; }

    public int LumaMode { get; private set; }

    /// <summary>The 16 subblock modes, raster order, when <see cref="IsSubblocks" />.</summary>
    public int[] SubblockModes { get; } = new int[16];

    public int ChromaMode { get; private set; }

    /// <param name="reader">The first partition, at this macroblock.</param>
    /// <param name="header">The frame header: segment map and skip probabilities.</param>
    /// <param name="above">The subblock modes along the bottom of the macroblocks above, four per macroblock column; updated.</param>
    /// <param name="left">The subblock modes along the right of the macroblock to the left; updated.</param>
    public void Read(Vp8BoolDecoder reader, Vp8FrameHeader header, Span<byte> above, Span<byte> left)
    {
        Segment = header.UpdateSegmentMap
            ? reader.ReadBit(header.SegmentProbabilities[0]) == 0
                ? reader.ReadBit(header.SegmentProbabilities[1])
                : reader.ReadBit(header.SegmentProbabilities[2]) + 2
            : 0;
        Skip = header.UseSkipProbability && reader.ReadBit(header.SkipProbability) == 1;

        IsSubblocks = reader.ReadBit(145) == 0;
        if (!IsSubblocks)
        {
            LumaMode = reader.ReadBit(156) == 1
                ? reader.ReadBit(128) == 1 ? Vp8Modes.Tm : Vp8Modes.H
                : reader.ReadBit(163) == 1 ? Vp8Modes.V : Vp8Modes.Dc;
            var context = (byte)Vp8Modes.AsSubblockMode(LumaMode);
            above.Fill(context);
            left.Fill(context);
        }
        else
        {
            var probabilities = Vp8Tables.BModeProbabilities;
            var tree = Vp8Tables.BModeTree;
            for (var y = 0; y < 4; y++)
            {
                int mode = left[y];
                for (var x = 0; x < 4; x++)
                {
                    var p = (above[x] * 10 + mode) * 9;
                    var i = 0;
                    while ((i = tree[i + reader.ReadBit(probabilities[p + (i >> 1)])]) > 0)
                    {
                    }

                    mode = -i;
                    above[x] = (byte)mode;
                    SubblockModes[y * 4 + x] = mode;
                }

                left[y] = (byte)mode;
            }
        }

        ChromaMode = reader.ReadBit(142) == 0 ? Vp8Modes.Dc
            : reader.ReadBit(114) == 0 ? Vp8Modes.V
            : reader.ReadBit(183) == 1 ? Vp8Modes.Tm : Vp8Modes.H;
    }
}
