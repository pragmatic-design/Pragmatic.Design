namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The quantizers and filter strengths of a frame's segments, from the quality setting and the analysis
///     (libwebp's <c>VP8SetSegmentParams</c> at its default spatial noise shaping of 50 and filter strength of
///     60): the quality maps to a base quantizer, each segment's susceptibility moves its own around it, and
///     segments that end up alike are merged.
/// </summary>
internal sealed class Vp8SegmentPlan
{
    private const int NoiseShaping = 50;
    private const int FilterStrengthSetting = 60;

    private Vp8SegmentPlan(Vp8Segment[] segments, int count, int uvDcDelta, int uvAcDelta, byte[] probabilities)
    {
        Segments = segments;
        Count = count;
        UvDcDelta = uvDcDelta;
        UvAcDelta = uvAcDelta;
        Probabilities = probabilities;
    }

    /// <summary>The four segments the header describes; past <see cref="Count" /> they repeat the last.</summary>
    public Vp8Segment[] Segments { get; }

    /// <summary>How many distinct segments there are; one means the frame is written without segmentation.</summary>
    public int Count { get; }

    public int UvDcDelta { get; }

    public int UvAcDelta { get; }

    /// <summary>The three probabilities of the segment tree, from how many macroblocks each segment has.</summary>
    public byte[] Probabilities { get; }

    /// <summary>Whether each macroblock's segment is written: there is more than one, used.</summary>
    public bool UpdateMap => Count > 1 && (Probabilities[0] != 255 || Probabilities[1] != 255 || Probabilities[2] != 255);

    /// <param name="quality">0 to 100.</param>
    /// <param name="analysis">The analysis; its segment map is renumbered when segments merge.</param>
    public static Vp8SegmentPlan Build(int quality, Vp8Analysis analysis)
    {
        var amplitude = 0.9 * NoiseShaping / 100.0 / 128.0;
        var compression = QualityToCompression(quality / 100.0);
        Span<int> quantizers = stackalloc int[Vp8Analysis.Segments];
        Span<int> strengths = stackalloc int[Vp8Analysis.Segments];
        for (var s = 0; s < Vp8Analysis.Segments; s++)
        {
            var exponent = 1.0 - amplitude * analysis.Alpha[s];
            var c = Math.Pow(compression, exponent);
            quantizers[s] = Math.Clamp((int)(127.0 * (1.0 - c)), 0, 127);

            // The filter follows the AC step; segments of lower complexity are filtered less.
            var step = Math.Min(Vp8Tables.AcQuantizer[quantizers[s]] >> 2, 63);
            var f = step * 5 * FilterStrengthSetting / (256 + analysis.Beta[s]);
            strengths[s] = f < 2 ? 0 : Math.Min(f, 63);
        }

        // Chroma: coarser where its susceptibility says it can take it, and a finer DC.
        var uvAc = (analysis.UvAlpha - 64) * (6 - -4) / (100 - 30);
        uvAc = Math.Clamp(uvAc * NoiseShaping / 100, -4, 6);
        var uvDc = Math.Clamp(-4 * NoiseShaping / 100, -15, 15);

        // Merge the segments that ended up with the same quantizer and filter.
        Span<int> map = stackalloc int[Vp8Analysis.Segments];
        var count = 1;
        map[0] = 0;
        for (var s = 1; s < Vp8Analysis.Segments; s++)
        {
            var found = -1;
            for (var t = 0; t < count; t++)
            {
                if (quantizers[t] == quantizers[s] && strengths[t] == strengths[s])
                {
                    found = t;
                    break;
                }
            }

            if (found >= 0)
            {
                map[s] = found;
                continue;
            }

            quantizers[count] = quantizers[s];
            strengths[count] = strengths[s];
            map[s] = count++;
        }

        var segmentOf = analysis.SegmentOfMacroblock;
        Span<int> sizes = stackalloc int[Vp8Analysis.Segments];
        for (var i = 0; i < segmentOf.Length; i++)
        {
            segmentOf[i] = (byte)map[segmentOf[i]];
            sizes[segmentOf[i]]++;
        }

        var segments = new Vp8Segment[Vp8Analysis.Segments];
        for (var s = 0; s < Vp8Analysis.Segments; s++)
        {
            var source = Math.Min(s, count - 1);
            segments[s] = s < count
                ? new Vp8Segment(quantizers[s], 0, 0, 0, uvDc, uvAc) { FilterStrength = strengths[s] }
                : segments[source];
        }

        byte[] probabilities =
        [
            Probability(sizes[0] + sizes[1], sizes[2] + sizes[3]),
            Probability(sizes[0], sizes[1]),
            Probability(sizes[2], sizes[3]),
        ];
        return new Vp8SegmentPlan(segments, count, uvDc, uvAc, probabilities);
    }

    /// <summary>
    ///     Raises each segment's filter strength to what its blockiest macroblock needs (libwebp's
    ///     <c>VP8AdjustFilterStrength</c>), and returns the frame's level: the highest.
    /// </summary>
    public int AdjustFilterStrengths()
    {
        var level = 0;
        for (var s = 0; s < Count; s++)
        {
            var segment = Segments[s];
            var needed = Math.Min((segment.MaxEdge * segment.Y2.AcStep) >> 3, 63);
            segment.FilterStrength = Math.Max(segment.FilterStrength, needed);
            level = Math.Max(level, segment.FilterStrength);
        }

        return level;
    }

    /// <summary>
    ///     JPEG-like quality to a compressibility: quality 75 is the middle, and the file size goes roughly as
    ///     the cube of the quantizer, so the curve is a cube root (libwebp's <c>QualityToCompression</c>).
    /// </summary>
    private static double QualityToCompression(double c)
    {
        var linear = c < 0.75 ? c * (2.0 / 3.0) : 2.0 * c - 1.0;
        return Math.Pow(linear, 1 / 3.0);
    }

    private static byte Probability(int a, int b)
    {
        var total = a + b;
        return total == 0 ? (byte)255 : (byte)((255 * a + total / 2) / total);
    }
}
