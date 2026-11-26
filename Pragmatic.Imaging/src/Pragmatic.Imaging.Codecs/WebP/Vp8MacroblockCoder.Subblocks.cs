using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The choice of a 4 x 4 mode for each luma block in turn, given up as soon as the macroblock costs more
///     than its 16 x 16 prediction (libwebp's <c>PickBestIntra4</c>).
/// </summary>
internal sealed partial class Vp8MacroblockCoder
{
    /// <summary>How much a 4 x 4 mode that leaves a block flat is charged, about a bit (libwebp's <c>FLATNESS_PENALTY</c>).</summary>
    private const int FlatnessPenalty = 140;

    private readonly short[] _subblockLevels = new short[16 * 16];

    /// <summary>
    ///     Returns whether 4 x 4 prediction beats the 16 x 16 choice already in <paramref name="decision" />; if it
    ///     does, the decision holds its modes and levels and the luma work buffer its reconstruction.
    /// </summary>
    [SkipLocalsInit]
    private bool PickSubblocks(int mbX, Vp8Segment segment, Vp8MacroblockDecision decision)
    {
        if (_maxSubblockHeaderBits == 0)
            return false;

        var reference = decision.Score.Value;
        var total = new Vp8Score { Header = Vp8Costs.SubblocksFlag };
        total.Weigh(segment.LambdaMode);

        Span<byte> above = stackalloc byte[4];
        Span<byte> left = stackalloc byte[4];
        _aboveNonZero.AsSpan(mbX * ContextCount, 4).CopyTo(above);
        _leftNonZero.AsSpan(0, 4).CopyTo(left);
        var aboveModes = _aboveModes.AsSpan(mbX * 4, 4);
        Span<int> modes = stackalloc int[16];
        Span<short> levels = stackalloc short[16];
        var headerBits = 0L;

        for (var block = 0; block < 16; block++)
        {
            int x = block & 3, y = block >> 2;
            var top = y == 0 ? aboveModes[x] : modes[block - 4];
            var leftMode = x == 0 ? _leftModes[y] : modes[block - 1];
            var offset = LumaBlock(block);

            var bestMode = -1;
            var best = Vp8Score.Worst;
            var bestNonZero = false;
            for (var mode = 0; mode < 10; mode++)
            {
                Vp8Prediction.Subblock(_y, offset, mode);
                var nonZero = Reconstruct4(segment, offset, levels);
                var score = new Vp8Score
                {
                    Distortion = Vp8Distortion.SumOfSquares4(_sourceY, _candidate, offset),
                    Header = Vp8Costs.SubblockMode(top, leftMode, mode),
                    Rate = mode > 0 && Vp8Distortion.IsFlat(levels, 1, 3) ? FlatnessPenalty : 0,
                };

                // The levels' rate costs most to compute: skip it for a mode already beaten without it.
                score.Weigh(segment.LambdaI4);
                if (bestMode >= 0 && score.Value >= best.Value)
                    continue;
                score.Rate += _costs.Residual(3, 0, above[x] + left[y], levels);
                score.Weigh(segment.LambdaI4);
                if (bestMode >= 0 && score.Value >= best.Value)
                    continue;

                best = score;
                bestMode = mode;
                bestNonZero = nonZero;
                CopyBlock(_candidate, _prediction, offset, 4);
                levels.CopyTo(_subblockLevels.AsSpan(block * 16, 16));
            }

            best.Weigh(segment.LambdaMode);
            total.Add(best);
            if (total.Value >= reference)
                return false;
            headerBits += best.Header;
            if (headerBits > _maxSubblockHeaderBits)
                return false;

            CopyBlock(_prediction, _y, offset, 4);
            modes[block] = bestMode;
            above[x] = left[y] = bestNonZero ? (byte)1 : (byte)0;
        }

        decision.IsLuma16 = false;
        decision.Score = total;
        for (var i = 0; i < 16; i++)
            decision.SubblockModes[i] = modes[i];
        _subblockLevels.CopyTo(decision.LumaLevels, 0);
        return true;
    }

    /// <summary>
    ///     Transforms and quantizes one 4 x 4 block against the prediction in the luma work buffer, into
    ///     <paramref name="levels" />, and writes its reconstruction to the candidate buffer.
    /// </summary>
    [SkipLocalsInit]
    private bool Reconstruct4(Vp8Segment segment, int offset, Span<short> levels)
    {
        Span<short> coefficients = stackalloc short[16];
        Vp8ForwardTransform.Dct(_sourceY, _y, offset, coefficients);
        var nonZero = segment.Y1.Quantize(coefficients, levels);
        CopyBlock(_y, _candidate, offset, 4);
        if (nonZero)
            Vp8InverseTransform.AddInverseDct(coefficients, _candidate, offset, S);
        return nonZero;
    }
}
