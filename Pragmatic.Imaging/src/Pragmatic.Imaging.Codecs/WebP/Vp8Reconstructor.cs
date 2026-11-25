namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Rebuilds each macroblock of a key frame from its prediction and residue, in work buffers laid out
///     like libwebp's: the row above and column to the left of the block hold its neighbours as they were
///     reconstructed, before the loop filter, and outside the frame the row above is 127 and the column to
///     the left 129 (RFC 6386, section 12.2). The result goes to the frame's planes, which the loop filter
///     then works on in place.
/// </summary>
internal sealed class Vp8Reconstructor
{
    private const int S = Vp8Prediction.Stride;
    private const int Origin = S + 8;

    private readonly Vp8Frame _frame;
    private readonly byte[] _y = new byte[S * 17];
    private readonly byte[] _u = new byte[S * 9];
    private readonly byte[] _v = new byte[S * 9];
    private byte[] _topY;
    private byte[] _topU;
    private byte[] _topV;
    private byte[] _nextTopY;
    private byte[] _nextTopU;
    private byte[] _nextTopV;

    public Vp8Reconstructor(Vp8Frame frame)
    {
        _frame = frame;
        _topY = new byte[frame.MacroblocksWide * 16];
        _topU = new byte[frame.MacroblocksWide * 8];
        _topV = new byte[frame.MacroblocksWide * 8];
        _nextTopY = new byte[_topY.Length];
        _nextTopU = new byte[_topU.Length];
        _nextTopV = new byte[_topV.Length];
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
        PrepareLuma(mbX, mbY);
        PrepareChroma(_u, _topU, mbX, mbY);
        PrepareChroma(_v, _topV, mbX, mbY);

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

    private void PrepareLuma(int mbX, int mbY)
    {
        var y = _y;
        var lastColumn = mbX == _frame.MacroblocksWide - 1;

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
            y[Origin - S - 1] = mbY > 0 ? _topY[mbX * 16 - 1] : (byte)127;
        }

        // The row above, and the four pixels to its right.
        if (mbY == 0)
        {
            y.AsSpan(Origin - S, 20).Fill(127);
        }
        else
        {
            _topY.AsSpan(mbX * 16, 16).CopyTo(y.AsSpan(Origin - S, 16));
            if (lastColumn)
                y.AsSpan(Origin - S + 16, 4).Fill(_topY[mbX * 16 + 15]);
            else
                _topY.AsSpan(mbX * 16 + 16, 4).CopyTo(y.AsSpan(Origin - S + 16, 4));
        }

        // Subblocks on the right column take their top-right pixels from the macroblock above and to the
        // right, whichever row of subblocks they are in.
        for (var row = 3; row < 15; row += 4)
            y.AsSpan(Origin - S + 16, 4).CopyTo(y.AsSpan(Origin + row * S + 16, 4));
    }

    private static void PrepareChroma(byte[] buffer, byte[] top, int mbX, int mbY)
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
            top.AsSpan(mbX * 8, 8).CopyTo(buffer.AsSpan(Origin - S, 8));
    }

    private static void Store(byte[] buffer, int size, byte[] plane, int stride, int x, int y, byte[] nextTop)
    {
        for (var j = 0; j < size; j++)
            buffer.AsSpan(Origin + j * S, size).CopyTo(plane.AsSpan((y + j) * stride + x, size));
        buffer.AsSpan(Origin + (size - 1) * S, size).CopyTo(nextTop.AsSpan(x, size));
    }
}
