using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Decides and reconstructs the macroblocks of a lossy frame in raster order, as libwebp does at its
///     default effort (rate-distortion choice of the 16 x 16, 4 x 4 and chroma modes, no trellis): each mode is
///     predicted from the neighbours as the decoder will have rebuilt them, its residue transformed and
///     quantized, and the one whose distortion and coded size weigh least is kept. The reconstruction it keeps
///     is exactly what the decoder will compute.
/// </summary>
internal sealed partial class Vp8MacroblockCoder
{
    private const int S = Vp8Prediction.Stride;
    private const int Origin = Vp8Borders.Origin;
    private const int ContextCount = Vp8Residuals.ContextCount;
    private const int Y2Context = 8;

    private readonly Vp8Costs _costs;
    private Vp8Picture _picture = null!;
    private int _maxSubblockHeaderBits;

    // The source of the macroblock, and its neighbours and reconstruction, laid out as Vp8Borders lays them.
    private readonly byte[] _sourceY = new byte[S * Vp8Borders.LumaRows];
    private readonly byte[] _sourceU = new byte[S * Vp8Borders.ChromaRows];
    private readonly byte[] _sourceV = new byte[S * Vp8Borders.ChromaRows];
    private readonly byte[] _y = new byte[S * Vp8Borders.LumaRows];
    private readonly byte[] _u = new byte[S * Vp8Borders.ChromaRows];
    private readonly byte[] _v = new byte[S * Vp8Borders.ChromaRows];

    // Scratch for the candidates: their predictions and reconstructions.
    private byte[] _prediction = new byte[S * Vp8Borders.LumaRows];
    private byte[] _candidate = new byte[S * Vp8Borders.LumaRows];
    private byte[] _best = new byte[S * Vp8Borders.LumaRows];
    private readonly byte[] _predictionU = new byte[S * Vp8Borders.ChromaRows];
    private readonly byte[] _predictionV = new byte[S * Vp8Borders.ChromaRows];
    private byte[] _candidateU = new byte[S * Vp8Borders.ChromaRows];
    private byte[] _candidateV = new byte[S * Vp8Borders.ChromaRows];
    private byte[] _bestU = new byte[S * Vp8Borders.ChromaRows];
    private byte[] _bestV = new byte[S * Vp8Borders.ChromaRows];

    private readonly short[] _coefficients = new short[16 * 16];
    private readonly short[] _levels = new short[16 * 16];
    private readonly short[] _y2Levels = new short[16];
    private readonly short[] _chromaLevels = new short[8 * 16];

    // The bottom row of the macroblock row above, and the one this row is writing for the next; and the
    // decoder's contexts above, per macroblock column. As wide as the picture: from the pool, frame by frame.
    private byte[] _topY = [];
    private byte[] _topU = [];
    private byte[] _topV = [];
    private byte[] _nextTopY = [];
    private byte[] _nextTopU = [];
    private byte[] _nextTopV = [];
    private byte[] _aboveModes = [];
    private byte[] _aboveNonZero = [];

    // The decoder's contexts to the left: subblock modes and non-zero flags.
    private readonly byte[] _leftModes = new byte[4];
    private readonly byte[] _leftNonZero = new byte[ContextCount];

    private bool _diffuseErrors;

    /// <param name="costs">The costs, which the caller refreshes as the probabilities are learned.</param>
    public Vp8MacroblockCoder(Vp8Costs costs) => _costs = costs;

    /// <summary>Starts a frame of <paramref name="picture" />.</summary>
    /// <param name="picture">The planes to encode.</param>
    /// <param name="maxSubblockHeaderBits">The most a macroblock's 4 x 4 modes may cost, in 1/256 bit; 0 forbids them.</param>
    /// <param name="diffuseErrors">Whether the chroma DC errors are diffused, as libwebp does below quality 99.</param>
    public void Begin(Vp8Picture picture, int maxSubblockHeaderBits, bool diffuseErrors)
    {
        Release();
        _picture = picture;
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

        // The contexts above the first row are all zero: no levels, DC modes, no error to diffuse.
        _aboveModes = pool.Rent(wide * 4);
        _aboveNonZero = pool.Rent(wide * ContextCount);
        _aboveError = ArrayPool<sbyte>.Shared.Rent(wide * 4);
        _aboveModes.AsSpan(0, wide * 4).Clear();
        _aboveNonZero.AsSpan(0, wide * ContextCount).Clear();
        _aboveError.AsSpan(0, wide * 4).Clear();
    }

    /// <summary>Gives the rows as wide as the picture back to the pool.</summary>
    public void Release()
    {
        var pool = ArrayPool<byte>.Shared;
        Return(pool, ref _topY);
        Return(pool, ref _topU);
        Return(pool, ref _topV);
        Return(pool, ref _nextTopY);
        Return(pool, ref _nextTopU);
        Return(pool, ref _nextTopV);
        Return(pool, ref _aboveModes);
        Return(pool, ref _aboveNonZero);
        if (_aboveError.Length > 0)
            ArrayPool<sbyte>.Shared.Return(_aboveError);
        _aboveError = [];
    }

    private static void Return(ArrayPool<byte> pool, ref byte[] buffer)
    {
        if (buffer.Length > 0)
            pool.Return(buffer);
        buffer = [];
    }

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
        // The picture holds this macroblock row only, from its first band row.
        CopyIn(_picture.Y, _picture.YStride, mbX * 16, Vp8Picture.FirstRow, 16, _sourceY);
        CopyIn(_picture.U, _picture.UvStride, mbX * 8, Vp8Picture.FirstRow, 8, _sourceU);
        CopyIn(_picture.V, _picture.UvStride, mbX * 8, Vp8Picture.FirstRow, 8, _sourceV);
    }

    private static void CopyIn(byte[] plane, int stride, int x, int y, int size, byte[] block)
    {
        for (var j = 0; j < size; j++)
            plane.AsSpan((y + j) * stride + x, size).CopyTo(block.AsSpan(Origin + j * S, size));
    }

    private static void CopyBlock(byte[] from, byte[] to, int size) => CopyBlock(from, to, Origin, size);

    /// <summary>Copies a block of 4, 8 or 16 pixels square, a row at a time in one move.</summary>
    private static void CopyBlock(byte[] from, byte[] to, int offset, int size)
    {
        var end = offset + (size - 1) * S + size;
        if (from.Length < end || to.Length < end)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var source = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(from), offset);
        ref var target = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(to), offset);
        for (var j = 0; j < size; j++)
        {
            ref var a = ref Unsafe.Add(ref source, j * S);
            ref var b = ref Unsafe.Add(ref target, j * S);
            switch (size)
            {
                case 4:
                    Unsafe.WriteUnaligned(ref b, Unsafe.ReadUnaligned<uint>(ref a));
                    break;
                case 8:
                    Unsafe.WriteUnaligned(ref b, Unsafe.ReadUnaligned<ulong>(ref a));
                    break;
                default:
                    Unsafe.WriteUnaligned(ref b, Unsafe.ReadUnaligned<Vector128<byte>>(ref a));
                    break;
            }
        }
    }

    /// <summary>The offset of 4 x 4 block <paramref name="block" /> (raster order) of a 16 x 16 macroblock.</summary>
    private static int LumaBlock(int block) => Origin + (block >> 2) * 4 * S + (block & 3) * 4;

    /// <summary>The offset of 4 x 4 block <paramref name="block" /> (0 to 3, raster order) of an 8 x 8 chroma block.</summary>
    private static int ChromaBlock(int block) => Origin + (block >> 1) * 4 * S + (block & 1) * 4;
}
