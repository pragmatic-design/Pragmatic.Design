using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Rebuilds each macroblock of a key frame from its prediction and residue, in work buffers whose
///     borders <see cref="Vp8Borders" /> fills. The result goes to the frame's planes, which the loop filter
///     then works on in place.
/// </summary>
/// <remarks>
///     The decoder keeps one per thread: the macroblock work buffers stay with it, and the rows along the
///     top, which depend on the frame's width, are rented by <see cref="Rent" /> and given back on dispose.
/// </remarks>
internal sealed class Vp8Reconstructor : IDisposable
{
    private const int S = Vp8Prediction.Stride;
    private const int Origin = Vp8Borders.Origin;

    private readonly byte[] _y = new byte[S * 17];
    private readonly byte[] _u = new byte[S * 9];
    private readonly byte[] _v = new byte[S * 9];
    private Vp8Frame _frame = null!;
    private byte[] _topY = [];
    private byte[] _topU = [];
    private byte[] _topV = [];
    private byte[] _nextTopY = [];
    private byte[] _nextTopU = [];
    private byte[] _nextTopV = [];

    /// <summary>Starts on <paramref name="frame" />, taking the rows along the top for its width from the pool.</summary>
    public void Rent(Vp8Frame frame)
    {
        _frame = frame;
        var pool = ArrayPool<byte>.Shared;
        _topY = pool.Rent(frame.MacroblocksWide * 16);
        _topU = pool.Rent(frame.MacroblocksWide * 8);
        _topV = pool.Rent(frame.MacroblocksWide * 8);
        _nextTopY = pool.Rent(frame.MacroblocksWide * 16);
        _nextTopU = pool.Rent(frame.MacroblocksWide * 8);
        _nextTopV = pool.Rent(frame.MacroblocksWide * 8);
    }

    /// <summary>Gives the rows back; a second call, or one before <see cref="Rent" />, does nothing.</summary>
    public void Dispose()
    {
        if (_topY.Length == 0)
            return;
        var pool = ArrayPool<byte>.Shared;
        pool.Return(_topY);
        pool.Return(_topU);
        pool.Return(_topV);
        pool.Return(_nextTopY);
        pool.Return(_nextTopU);
        pool.Return(_nextTopV);
        _topY = _topU = _topV = _nextTopY = _nextTopU = _nextTopV = [];
        _frame = null!;
    }

    /// <summary>Called after the last macroblock of a row: its bottom samples become the next row's top.</summary>
    public void EndRow()
    {
        (_topY, _nextTopY) = (_nextTopY, _topY);
        (_topU, _nextTopU) = (_nextTopU, _topU);
        (_topV, _nextTopV) = (_nextTopV, _topV);
    }

    public void Reconstruct(int mbX, int mbY, Vp8MacroblockModes modes, ReadOnlySpan<short> coefficients, int nonZero)
    {
        Vp8Borders.PrepareLuma(_y, _topY, mbX, mbY, _frame.MacroblocksWide);
        Vp8Borders.PrepareChroma(_u, _topU, mbX, mbY);
        Vp8Borders.PrepareChroma(_v, _topV, mbX, mbY);

        if (modes.IsSubblocks)
        {
            for (var block = 0; block < 16; block++)
            {
                var at = Origin + (block >> 2) * 4 * S + (block & 3) * 4;
                Vp8Prediction.Subblock(_y, at, modes.SubblockModes[block]);
                if ((nonZero & (1 << block)) != 0)
                    Vp8InverseTransform.AddInverseDct(coefficients.Slice(block * 16, 16), _y, at, S);
            }
        }
        else
        {
            Vp8Prediction.Block(_y, Origin, 16, modes.LumaMode, mbY > 0, mbX > 0);
            for (var block = 0; block < 16; block++)
            {
                if ((nonZero & (1 << block)) != 0)
                    Vp8InverseTransform.AddInverseDct(coefficients.Slice(block * 16, 16), _y, Origin + (block >> 2) * 4 * S + (block & 3) * 4, S);
            }
        }

        Chroma(_u, modes.ChromaMode, mbX, mbY, coefficients, nonZero, 16);
        Chroma(_v, modes.ChromaMode, mbX, mbY, coefficients, nonZero, 20);

        Store(_y, 16, _frame.Y, _frame.YStride, mbX * 16, mbY * 16, _nextTopY);
        Store(_u, 8, _frame.U, _frame.UvStride, mbX * 8, mbY * 8, _nextTopU);
        Store(_v, 8, _frame.V, _frame.UvStride, mbX * 8, mbY * 8, _nextTopV);
    }

    private static void Chroma(byte[] buffer, int mode, int mbX, int mbY, ReadOnlySpan<short> coefficients, int nonZero, int firstBlock)
    {
        Vp8Prediction.Block(buffer, Origin, 8, mode, mbY > 0, mbX > 0);
        for (var i = 0; i < 4; i++)
        {
            var block = firstBlock + i;
            if ((nonZero & (1 << block)) != 0)
                Vp8InverseTransform.AddInverseDct(coefficients.Slice(block * 16, 16), buffer, Origin + (i >> 1) * 4 * S + (i & 1) * 4, S);
        }
    }

    private static void Store(byte[] buffer, int size, byte[] plane, int stride, int x, int y, byte[] nextTop)
    {
        for (var j = 0; j < size; j++)
            buffer.AsSpan(Origin + j * S, size).CopyTo(plane.AsSpan((y + j) * stride + x, size));
        buffer.AsSpan(Origin + (size - 1) * S, size).CopyTo(nextTop.AsSpan(x, size));
    }
}
