using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Each macroblock's segment and intra modes, kept until the first partition is written after the tokens'
///     probabilities are known, and written there as the decoder's <see cref="Vp8MacroblockModes" /> reads them.
///     The storage comes from the shared array pool; dispose it to give it back.
/// </summary>
internal sealed class Vp8EncodedModes(int macroblocks) : IDisposable
{
    private const int Size = 19; // segment, kind, 16 luma modes, chroma mode
    private const int Luma16 = 0xFF;

    private readonly byte[] _modes = ArrayPool<byte>.Shared.Rent(macroblocks * Size);
    private readonly int _macroblocks = macroblocks;

    public void Dispose() => ArrayPool<byte>.Shared.Return(_modes);

    public void Store(int index, int segment, Vp8MacroblockDecision decision)
    {
        var entry = _modes.AsSpan(index * Size, Size);
        entry[0] = (byte)segment;
        if (decision.IsLuma16)
        {
            entry[1] = Luma16;
            entry[2] = (byte)decision.Luma16Mode;
        }
        else
        {
            entry[1] = 0;
            for (var i = 0; i < 16; i++)
                entry[2 + i] = (byte)decision.SubblockModes[i];
        }

        entry[18] = (byte)decision.ChromaMode;
    }

    /// <summary>Writes every macroblock's modes, raster order, with the segment tree when the map is written.</summary>
    public void Write(Vp8BoolEncoder output, int macroblocksWide, Vp8SegmentPlan plan)
    {
        var aboveModes = new byte[macroblocksWide * 4];
        Span<byte> leftModes = stackalloc byte[4];
        var updateMap = plan.UpdateMap;
        var segmentProbabilities = plan.Probabilities;
        var macroblocks = _macroblocks;
        for (var index = 0; index < macroblocks; index++)
        {
            var mbX = index % macroblocksWide;
            if (mbX == 0)
                leftModes.Clear();
            var entry = _modes.AsSpan(index * Size, Size);
            if (updateMap)
            {
                var segment = entry[0];
                output.PutBit(segment >= 2 ? 1 : 0, segmentProbabilities[0]);
                output.PutBit(segment & 1, segment >= 2 ? segmentProbabilities[2] : segmentProbabilities[1]);
            }

            var above = aboveModes.AsSpan(mbX * 4, 4);
            if (entry[1] == Luma16)
            {
                var mode = entry[2];
                output.PutBit(1, 145);
                if (mode is Vp8Modes.Tm or Vp8Modes.H)
                {
                    output.PutBit(1, 156);
                    output.PutBit(mode == Vp8Modes.Tm ? 1 : 0, 128);
                }
                else
                {
                    output.PutBit(0, 156);
                    output.PutBit(mode == Vp8Modes.V ? 1 : 0, 163);
                }

                var context = (byte)Vp8Modes.AsSubblockMode(mode);
                above.Fill(context);
                leftModes.Fill(context);
            }
            else
            {
                output.PutBit(0, 145);
                var probabilities = Vp8Tables.BModeProbabilities;
                for (var y = 0; y < 4; y++)
                {
                    int left = leftModes[y];
                    for (var x = 0; x < 4; x++)
                    {
                        int mode = entry[2 + y * 4 + x];
                        Vp8SubblockModeTree.Write(output, probabilities.Slice((above[x] * 10 + left) * 9, 9), mode);
                        above[x] = (byte)mode;
                        left = mode;
                    }

                    leftModes[y] = (byte)left;
                }
            }

            var chroma = entry[18];
            output.PutBit(chroma != Vp8Modes.Dc ? 1 : 0, 142);
            if (chroma == Vp8Modes.Dc)
                continue;
            output.PutBit(chroma != Vp8Modes.V ? 1 : 0, 114);
            if (chroma != Vp8Modes.V)
                output.PutBit(chroma == Vp8Modes.Tm ? 1 : 0, 183);
        }
    }
}
