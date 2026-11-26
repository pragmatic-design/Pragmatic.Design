namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What the encoder holds for one segment of the frame: its quantizer index and the quantization of each
///     kind of block, the rate-distortion weights derived from them (libwebp's <c>SetupMatrices</c>), and the
///     loop filter strength it is written with. An encoder reuses its segments image after image:
///     <see cref="Set" /> makes one anew.
/// </summary>
internal sealed class Vp8Segment
{
    public int Quantizer { get; private set; }

    public Vp8QuantMatrix Y1 { get; } = new(4, 4, 0);

    public Vp8QuantMatrix Y2 { get; } = new(8, 8, 1);

    public Vp8QuantMatrix Uv { get; } = new(4, 4, 2);

    /// <summary>The weight of rate against distortion in choosing each 4 x 4 mode.</summary>
    public int LambdaI4 { get; private set; }

    /// <summary>The weight in choosing the 16 x 16 mode.</summary>
    public int LambdaI16 { get; private set; }

    /// <summary>The weight in choosing the chroma mode.</summary>
    public int LambdaUv { get; private set; }

    /// <summary>The weight in choosing between 16 x 16 and 4 x 4 prediction.</summary>
    public int LambdaMode { get; private set; }

    /// <summary>The distortion above which a macroblock of only DC is counted as blocky, for the filter.</summary>
    public int MinDistortion { get; private set; }

    /// <summary>The largest step between the DC of neighbouring 4 x 4 blocks in a blocky macroblock.</summary>
    public int MaxEdge { get; set; }

    /// <summary>The loop filter strength, 0 to 63.</summary>
    public int FilterStrength { get; set; }

    /// <summary>Makes this the segment of <paramref name="quantizer" /> and the deltas, its filter at <paramref name="filterStrength" />.</summary>
    public void Set(int quantizer, int yDcDelta, int y2DcDelta, int y2AcDelta, int uvDcDelta, int uvAcDelta, int filterStrength)
    {
        Quantizer = quantizer;
        var dc = Vp8Tables.DcQuantizer;
        var ac = Vp8Tables.AcQuantizer;
        Y1.Set(dc[Clip(quantizer + yDcDelta, 127)], ac[Clip(quantizer, 127)], 0);
        Y2.Set(dc[Clip(quantizer + y2DcDelta, 127)] * 2, Math.Max((ac[Clip(quantizer + y2AcDelta, 127)] * 101581) >> 16, 8), 1);
        Uv.Set(dc[Clip(quantizer + uvDcDelta, 117)], ac[Clip(quantizer + uvAcDelta, 127)], 2);

        var qI4 = Y1.AverageStep;
        var qI16 = Y2.AverageStep;
        var qUv = Uv.AverageStep;
        LambdaI4 = Math.Max((3 * qI4 * qI4) >> 7, 1);
        LambdaI16 = Math.Max(3 * qI16 * qI16, 1);
        LambdaUv = Math.Max((3 * qUv * qUv) >> 6, 1);
        LambdaMode = Math.Max((qI4 * qI4) >> 7, 1);
        MinDistortion = 20 * Y1.DcStep;
        MaxEdge = 0;
        FilterStrength = filterStrength;
    }

    private static int Clip(int value, int max) => value < 0 ? 0 : value > max ? max : value;
}
