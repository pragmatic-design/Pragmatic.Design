// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>The choice of the 16 x 16 luma mode, its DC coded in the Y2 block (libwebp's <c>PickBestIntra16</c>).</summary>
internal sealed partial class Vp8MacroblockCoder
{
    private void PickLuma16(int mbX, int mbY, Vp8Segment segment, Vp8MacroblockDecision decision)
    {
        var flat = Vp8Distortion.IsFlatSource(_sourceY, Origin);
        var best = Vp8Score.Worst;
        var bestNonZero = 0;
        for (var mode = 0; mode < 4; mode++)
        {
            Vp8Prediction.Block(_y, Origin, 16, mode, mbY > 0, mbX > 0);
            CopyBlock(_y, _prediction, 16);
            var nonZero = Reconstruct16(segment, _prediction, _candidate);

            var score = new Vp8Score
            {
                Distortion = Vp8Distortion.SumOfSquares(_sourceY, _candidate, Origin, 16, 16),
                Header = Vp8Costs.Luma16Mode(mode),
            };
            if (flat)
            {
                // A flat source predicted by a mode that leaves no AC: keep its distortion very low.
                flat = Vp8Distortion.IsFlat(_levels, 16, 0);
                if (flat)
                    score.Distortion *= 2;
            }

            // The rate only adds: a mode already beaten without it is beaten with it.
            score.Weigh(segment.LambdaI16);
            if (mode > 0 && score.Value >= best.Value)
                continue;
            score.Rate = Luma16Rate(mbX);
            score.Weigh(segment.LambdaI16);
            if (mode == 0 || score.Value < best.Value)
            {
                best = score;
                bestNonZero = nonZero;
                (_candidate, _best) = (_best, _candidate);
                decision.Luma16Mode = mode;
                _levels.CopyTo(decision.LumaLevels, 0);
                _y2Levels.CopyTo(decision.Y2Levels, 0);
            }
        }

        best.Weigh(segment.LambdaMode);
        decision.Score = best;
        decision.IsLuma16 = true;

        // A blocky macroblock (only its DC coded) with a fair distortion: the filter will need to smooth edges
        // as large as its DC steps.
        if ((bestNonZero & 0x100FFFF) == 0x1000000 && best.Distortion > segment.MinDistortion)
        {
            var y2 = decision.Y2Levels;
            var edge = Math.Max(Math.Abs((int)y2[1]), Math.Max(Math.Abs((int)y2[2]), Math.Abs((int)y2[4])));
            segment.MaxEdge = Math.Max(segment.MaxEdge, edge);
        }
    }

    /// <summary>
    ///     Transforms and quantizes the luma against <paramref name="prediction" />, into <see cref="_levels" />
    ///     and <see cref="_y2Levels" />, and writes what the decoder will rebuild to <paramref name="reconstruction" />.
    ///     Returns which blocks have levels: bit <c>n</c> for luma block <c>n</c>, bit 24 for the Y2 block.
    /// </summary>
    [SkipLocalsInit]
    private int Reconstruct16(Vp8Segment segment, byte[] prediction, byte[] reconstruction)
    {
        var coefficients = _coefficients.AsSpan();
        for (var n = 0; n < 16; n += 2)
            Vp8ForwardTransform.Dct2(_sourceY, prediction, LumaBlock(n), coefficients.Slice(n * 16, 32));

        Span<short> y2 = stackalloc short[16];
        Vp8ForwardTransform.Walsh(coefficients, y2);
        var nonZero = segment.Y2.Quantize(y2, _y2Levels) ? 1 << 24 : 0;
        for (var n = 0; n < 16; n++)
        {
            if (segment.Y1.Quantize(coefficients.Slice(n * 16, 16), _levels.AsSpan(n * 16, 16), first: 1))
                nonZero |= 1 << n;
        }

        Vp8InverseTransform.Walsh(y2, coefficients);

        // Two side-by-side blocks at a time, written straight from the prediction; a pair with neither AC
        // levels nor a DC from the Y2 block is the prediction.
        for (var n = 0; n < 16; n += 2)
        {
            if ((nonZero & (3 << n)) != 0 || coefficients[n * 16] != 0 || coefficients[n * 16 + 16] != 0)
                Vp8InverseTransform.AddInverseDct2(coefficients.Slice(n * 16, 32), prediction, reconstruction, LumaBlock(n), S);
            else
                CopyPair(prediction, reconstruction, LumaBlock(n));
        }

        return nonZero;
    }

    /// <summary>The rate of the Y2 and luma levels just quantized, in the contexts around this macroblock.</summary>
    [SkipLocalsInit]
    private long Luma16Rate(int mbX)
    {
        Span<byte> above = stackalloc byte[ContextCount];
        Span<byte> left = stackalloc byte[ContextCount];
        _aboveNonZero.AsSpan(mbX * ContextCount, ContextCount).CopyTo(above);
        _leftNonZero.CopyTo(left);

        long rate = _costs.Residual(1, 0, above[Y2Context] + left[Y2Context], _y2Levels);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var levels = _levels.AsSpan((y * 4 + x) * 16, 16);
                rate += _costs.Residual(0, 1, above[x] + left[y], levels, out var coded);
                var flag = coded ? (byte)1 : (byte)0;
                above[x] = left[y] = flag;
            }
        }

        return rate;
    }
}
