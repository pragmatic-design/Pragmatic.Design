using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Decides and reconstructs the macroblocks of a lossy frame in raster order, as libwebp does at its
///     default effort (rate-distortion choice of the 16 x 16, 4 x 4 and chroma modes, no trellis): each mode is
///     predicted from the neighbours as the decoder will have rebuilt them, its residue transformed and
///     quantized, and the one whose distortion and coded size weigh least is kept. The reconstruction it keeps
///     is exactly what the decoder will compute.
/// </summary>
internal sealed partial class Vp8MacroblockCoder : IDisposable
{
    private const int S = Vp8Prediction.Stride;
    private const int Origin = Vp8Borders.Origin;
    private const int ContextCount = Vp8Residuals.ContextCount;
    private const int Y2Context = 8;

    private readonly Vp8Picture _picture;
    private readonly Vp8Costs _costs;
    private readonly int _maxSubblockHeaderBits;

    // The source of the macroblock, and its neighbours and reconstruction, laid out as Vp8Borders lays them.
    private readonly byte[] _sourceY = Rent(S * Vp8Borders.LumaRows);
    private readonly byte[] _sourceU = Rent(S * Vp8Borders.ChromaRows);
    private readonly byte[] _sourceV = Rent(S * Vp8Borders.ChromaRows);
    private readonly byte[] _y = Rent(S * Vp8Borders.LumaRows);
    private readonly byte[] _u = Rent(S * Vp8Borders.ChromaRows);
    private readonly byte[] _v = Rent(S * Vp8Borders.ChromaRows);

    // Scratch for the candidates: their predictions and reconstructions.
    private readonly byte[] _prediction = Rent(S * Vp8Borders.LumaRows);
    private byte[] _candidate = Rent(S * Vp8Borders.LumaRows);
    private byte[] _best = Rent(S * Vp8Borders.LumaRows);
    private readonly byte[] _predictionU = Rent(S * Vp8Borders.ChromaRows);
    private readonly byte[] _predictionV = Rent(S * Vp8Borders.ChromaRows);
    private byte[] _candidateU = Rent(S * Vp8Borders.ChromaRows);
    private byte[] _candidateV = Rent(S * Vp8Borders.ChromaRows);
    private byte[] _bestU = Rent(S * Vp8Borders.ChromaRows);
    private byte[] _bestV = Rent(S * Vp8Borders.ChromaRows);

    private readonly short[] _coefficients = new short[16 * 16];
    private readonly short[] _levels = new short[16 * 16];
    private readonly short[] _y2Levels = new short[16];
    private readonly short[] _chromaLevels = new short[8 * 16];

    // The bottom row of the macroblock row above, and the one this row is writing for the next.
    private byte[] _topY;
    private byte[] _topU;
    private byte[] _topV;
    private byte[] _nextTopY;
    private byte[] _nextTopU;
    private byte[] _nextTopV;

    // The decoder's contexts: subblock modes and non-zero flags above (per macroblock column) and to the left.
    private readonly byte[] _aboveModes;
    private readonly byte[] _aboveNonZero;
    private readonly byte[] _leftModes = new byte[4];
    private readonly byte[] _leftNonZero = new byte[ContextCount];

    private readonly bool _diffuseErrors;

    /// <param name="picture">The planes to encode.</param>
    /// <param name="costs">The costs, which the caller refreshes as the probabilities are learned.</param>
    /// <param name="maxSubblockHeaderBits">The most a macroblock's 4 x 4 modes may cost, in 1/256 bit; 0 forbids them.</param>
    /// <param name="diffuseErrors">Whether the chroma DC errors are diffused, as libwebp does below quality 99.</param>
    public Vp8MacroblockCoder(Vp8Picture picture, Vp8Costs costs, int maxSubblockHeaderBits, bool diffuseErrors)
    {
        _picture = picture;
        _costs = costs;
        _maxSubblockHeaderBits = maxSubblockHeaderBits;
        _diffuseErrors = diffuseErrors;
        var wide = picture.MacroblocksWide;
        var pool = ArrayPool<byte>.Shared;
        _topY = pool.Rent(wide * 16);
        _topU = pool.Rent(wide * 8);
        _topV = pool.Rent(wide * 8);
        _nextTopY = pool.Rent(wide * 16);
        _nextTopU = pool.Rent(wide * 8);
        _nextTopV = pool.Rent(wide * 8);
        _aboveModes = new byte[wide * 4];
        _aboveNonZero = new byte[wide * ContextCount];
        _aboveError = new sbyte[wide * 4];
    }

    public void Dispose()
    {
        var pool = ArrayPool<byte>.Shared;
        foreach (var buffer in (byte[][])[
                     _topY, _topU, _topV, _nextTopY, _nextTopU, _nextTopV,
                     _sourceY, _sourceU, _sourceV, _y, _u, _v,
                     _prediction, _candidate, _best, _predictionU, _predictionV, _candidateU, _candidateV, _bestU, _bestV,
                 ])
            pool.Return(buffer);
    }

    /// <summary>A work buffer from the pool. Whatever it held is overwritten before it is read.</summary>
    private static byte[] Rent(int length) => ArrayPool<byte>.Shared.Rent(length);

    /// <summary>Called before the first macroblock of a row.</summary>
    public void StartRow()
    {
        _leftModes.AsSpan().Clear();
        _leftNonZero.AsSpan().Clear();
        _leftError.AsSpan().Clear();
    }

    /// <summary>Called after the last macroblock of a row: its bottom samples become the next row's top.</summary>
    public void EndRow()
    {
        (_topY, _nextTopY) = (_nextTopY, _topY);
        (_topU, _nextTopU) = (_nextTopU, _topU);
        (_topV, _nextTopV) = (_nextTopV, _topV);
    }

    /// <summary>Chooses the modes and levels of macroblock (<paramref name="mbX" />, <paramref name="mbY" />) and reconstructs it.</summary>
    public void Decide(int mbX, int mbY, Vp8Segment segment, Vp8MacroblockDecision decision)
    {
        Load(mbX, mbY);
        PickLuma16(mbX, mbY, segment, decision);
        if (!PickSubblocks(mbX, segment, decision))
            CopyBlock(_best, _y, 16);
        PickChroma(mbX, mbY, segment, decision);
    }

    /// <summary>
    ///     Records the decided macroblock's tokens, in the contexts the decoder will read them in, and keeps its
    ///     reconstruction as the neighbour of the macroblocks after it.
    /// </summary>
    public void Commit(int mbX, Vp8MacroblockDecision decision, Vp8Tokens tokens)
    {
        var above = _aboveNonZero.AsSpan(mbX * ContextCount, ContextCount);
        var left = _leftNonZero.AsSpan();
        var lumaType = 3;
        var first = 0;
        if (decision.IsLuma16)
        {
            var flag = tokens.Record(1, 0, above[Y2Context] + left[Y2Context], decision.Y2Levels) ? (byte)1 : (byte)0;
            above[Y2Context] = left[Y2Context] = flag;
            lumaType = 0;
            first = 1;
        }

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var flag = tokens.Record(lumaType, first, above[x] + left[y], decision.LumaLevels.AsSpan((y * 4 + x) * 16, 16)) ? (byte)1 : (byte)0;
                above[x] = left[y] = flag;
            }
        }

        for (var plane = 0; plane < 2; plane++)
        {
            var offset = 4 + plane * 2;
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var block = plane * 4 + y * 2 + x;
                    var flag = tokens.Record(2, 0, above[offset + x] + left[offset + y], decision.ChromaLevels.AsSpan(block * 16, 16)) ? (byte)1 : (byte)0;
                    above[offset + x] = left[offset + y] = flag;
                }
            }
        }

        var aboveModes = _aboveModes.AsSpan(mbX * 4, 4);
        if (decision.IsLuma16)
        {
            var context = (byte)Vp8Modes.AsSubblockMode(decision.Luma16Mode);
            aboveModes.Fill(context);
            _leftModes.AsSpan().Fill(context);
        }
        else
        {
            for (var i = 0; i < 4; i++)
            {
                aboveModes[i] = (byte)decision.SubblockModes[12 + i];
                _leftModes[i] = (byte)decision.SubblockModes[i * 4 + 3];
            }
        }

        _y.AsSpan(Origin + 15 * S, 16).CopyTo(_nextTopY.AsSpan(mbX * 16, 16));
        _u.AsSpan(Origin + 7 * S, 8).CopyTo(_nextTopU.AsSpan(mbX * 8, 8));
        _v.AsSpan(Origin + 7 * S, 8).CopyTo(_nextTopV.AsSpan(mbX * 8, 8));
    }

    private void Load(int mbX, int mbY)
    {
        Vp8Borders.PrepareLuma(_y, _topY, mbX, mbY, _picture.MacroblocksWide);
        Vp8Borders.PrepareChroma(_u, _topU, mbX, mbY);
        Vp8Borders.PrepareChroma(_v, _topV, mbX, mbY);
        CopyIn(_picture.Y, _picture.YStride, mbX * 16, mbY * 16, 16, _sourceY);
        CopyIn(_picture.U, _picture.UvStride, mbX * 8, mbY * 8, 8, _sourceU);
        CopyIn(_picture.V, _picture.UvStride, mbX * 8, mbY * 8, 8, _sourceV);
    }

    private static void CopyIn(byte[] plane, int stride, int x, int y, int size, byte[] block)
    {
        for (var j = 0; j < size; j++)
            plane.AsSpan((y + j) * stride + x, size).CopyTo(block.AsSpan(Origin + j * S, size));
    }

    private static void CopyBlock(byte[] from, byte[] to, int size)
    {
        for (var j = 0; j < size; j++)
            from.AsSpan(Origin + j * S, size).CopyTo(to.AsSpan(Origin + j * S, size));
    }

    private static void CopyBlock(byte[] from, byte[] to, int offset, int size)
    {
        for (var j = 0; j < size; j++)
            from.AsSpan(offset + j * S, size).CopyTo(to.AsSpan(offset + j * S, size));
    }

    /// <summary>The offset of 4 x 4 block <paramref name="block" /> (raster order) of a 16 x 16 macroblock.</summary>
    private static int LumaBlock(int block) => Origin + (block >> 2) * 4 * S + (block & 3) * 4;

    /// <summary>The offset of 4 x 4 block <paramref name="block" /> (0 to 3, raster order) of an 8 x 8 chroma block.</summary>
    private static int ChromaBlock(int block) => Origin + (block >> 1) * 4 * S + (block & 1) * 4;
}
