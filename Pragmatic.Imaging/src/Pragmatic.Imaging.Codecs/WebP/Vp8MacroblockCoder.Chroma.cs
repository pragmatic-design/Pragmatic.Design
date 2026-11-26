using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The choice of the chroma mode, one for U and V together (libwebp's <c>PickBestUV</c>), with the error
///     of each chroma DC carried into the blocks below and to the right (libwebp's DC error diffusion), so a
///     smooth gradient quantized coarsely does not turn into flat steps.
/// </summary>
internal sealed partial class Vp8MacroblockCoder
{
    // Diffusion weights out of 16: 7 to the block below, 8 to the block on the right; 15/16 of the error is
    // passed on, so a near-lossless frame does not oscillate into a checkerboard.
    private const int BelowWeight = 7;
    private const int RightWeight = 8;

    // Per macroblock column and to the left, for U then V: the two errors that reach the next blocks.
    private readonly sbyte[] _aboveError;
    private readonly sbyte[] _leftError = new sbyte[4];
    private readonly int[] _candidateError = new int[6];
    private readonly int[] _bestError = new int[6];

    private void PickChroma(int mbX, int mbY, Vp8Segment segment, Vp8MacroblockDecision decision)
    {
        var best = Vp8Score.Worst;
        for (var mode = 0; mode < 4; mode++)
        {
            Vp8Prediction.Block(_u, Origin, 8, mode, mbY > 0, mbX > 0);
            Vp8Prediction.Block(_v, Origin, 8, mode, mbY > 0, mbX > 0);
            CopyBlock(_u, _predictionU, 8);
            CopyBlock(_v, _predictionV, 8);
            ReconstructChroma(mbX, segment);

            var score = new Vp8Score
            {
                Distortion = Vp8Distortion.SumOfSquares(_sourceU, _candidateU, Origin, 8, 8) +
                             Vp8Distortion.SumOfSquares(_sourceV, _candidateV, Origin, 8, 8),
                Header = Vp8Costs.ChromaMode(mode),
            };
            if (mode > 0 && Vp8Distortion.IsFlat(_chromaLevels, 8, 2))
                score.Rate += FlatnessPenalty * 8;

            // The levels' rate only adds: a mode already beaten without it is beaten with it.
            score.Weigh(segment.LambdaUv);
            if (mode > 0 && score.Value >= best.Value)
                continue;
            score.Rate += ChromaRate(mbX);
            score.Weigh(segment.LambdaUv);
            if (mode == 0 || score.Value < best.Value)
            {
                best = score;
                (_candidateU, _bestU) = (_bestU, _candidateU);
                (_candidateV, _bestV) = (_bestV, _candidateV);
                decision.ChromaMode = mode;
                _chromaLevels.CopyTo(decision.ChromaLevels, 0);
                _candidateError.CopyTo(_bestError, 0);
            }
        }

        CopyBlock(_bestU, _u, 8);
        CopyBlock(_bestV, _v, 8);
        decision.Score.Add(best);
        if (_diffuseErrors)
            StoreErrors(mbX);
    }

    /// <summary>Transforms and quantizes U and V against their predictions, into <see cref="_chromaLevels" />, and reconstructs them.</summary>
    [SkipLocalsInit]
    private void ReconstructChroma(int mbX, Vp8Segment segment)
    {
        Span<short> coefficients = stackalloc short[4 * 16];
        for (var plane = 0; plane < 2; plane++)
        {
            var source = plane == 0 ? _sourceU : _sourceV;
            var prediction = plane == 0 ? _predictionU : _predictionV;
            var reconstruction = plane == 0 ? _candidateU : _candidateV;
            for (var block = 0; block < 4; block++)
                Vp8ForwardTransform.Dct(source, prediction, ChromaBlock(block), coefficients.Slice(block * 16, 16));
            if (_diffuseErrors)
                DiffuseErrors(mbX, plane, segment.Uv, coefficients);

            CopyBlock(prediction, reconstruction, 8);
            for (var block = 0; block < 4; block++)
            {
                var block16 = coefficients.Slice(block * 16, 16);
                if (segment.Uv.Quantize(block16, _chromaLevels.AsSpan((plane * 4 + block) * 16, 16)))
                    Vp8InverseTransform.AddInverseDct(block16, reconstruction, ChromaBlock(block), S);
            }
        }
    }

    /// <summary>
    ///     Adds the errors from above and from the left to the DC of each of the four blocks of one plane,
    ///     quantizing each in turn so its own error reaches the next (libwebp's <c>CorrectDCValues</c>).
    /// </summary>
    private void DiffuseErrors(int mbX, int plane, Vp8QuantMatrix matrix, Span<short> coefficients)
    {
        var top = _aboveError.AsSpan(mbX * 4 + plane * 2, 2);
        var left = _leftError.AsSpan(plane * 2, 2);
        coefficients[0] += (short)((BelowWeight * top[0] + RightWeight * left[0]) >> 3);
        var error0 = matrix.QuantizeDc(ref coefficients[0]);
        coefficients[16] += (short)((BelowWeight * top[1] + RightWeight * error0) >> 3);
        var error1 = matrix.QuantizeDc(ref coefficients[16]);
        coefficients[32] += (short)((BelowWeight * error0 + RightWeight * left[1]) >> 3);
        var error2 = matrix.QuantizeDc(ref coefficients[32]);
        coefficients[48] += (short)((BelowWeight * error1 + RightWeight * error2) >> 3);
        var error3 = matrix.QuantizeDc(ref coefficients[48]);
        _candidateError[plane * 3] = error1;
        _candidateError[plane * 3 + 1] = error2;
        _candidateError[plane * 3 + 2] = error3;
    }

    /// <summary>Passes the chosen mode's edge errors on: the right ones to the next macroblock, the bottom ones to the one below.</summary>
    private void StoreErrors(int mbX)
    {
        for (var plane = 0; plane < 2; plane++)
        {
            var error1 = _bestError[plane * 3];
            var error2 = _bestError[plane * 3 + 1];
            var error3 = _bestError[plane * 3 + 2];
            var left = (3 * error3) >> 2;
            _leftError[plane * 2] = (sbyte)error1;
            _leftError[plane * 2 + 1] = (sbyte)left;
            _aboveError[mbX * 4 + plane * 2] = (sbyte)error2;
            _aboveError[mbX * 4 + plane * 2 + 1] = (sbyte)(error3 - left);
        }
    }

    /// <summary>The rate of the chroma levels just quantized, in the contexts around this macroblock.</summary>
    [SkipLocalsInit]
    private long ChromaRate(int mbX)
    {
        Span<byte> above = stackalloc byte[4];
        Span<byte> left = stackalloc byte[4];
        _aboveNonZero.AsSpan(mbX * ContextCount + 4, 4).CopyTo(above);
        _leftNonZero.AsSpan(4, 4).CopyTo(left);
        long rate = 0;
        for (var plane = 0; plane < 2; plane++)
        {
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var levels = _chromaLevels.AsSpan((plane * 4 + y * 2 + x) * 16, 16);
                    rate += _costs.Residual(2, 0, above[plane * 2 + x] + left[plane * 2 + y], levels);
                    above[plane * 2 + x] = left[plane * 2 + y] = levels.ContainsAnyExcept((short)0) ? (byte)1 : (byte)0;
                }
            }
        }

        return rate;
    }
}
