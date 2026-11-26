namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Encodes a lossy VP8 key frame (RFC 6386) the way libwebp does at its default settings: the picture is
///     analysed into segments, every macroblock's modes are chosen by rate and distortion with the token
///     probabilities refreshed as they are learned, and the frame is written with the probabilities that fit
///     its tokens best and a loop filter as strong as its blockiest macroblocks need.
/// </summary>
internal static class Vp8Encoder
{
    /// <summary>The largest first partition the frame tag can say: its size is 19 bits.</summary>
    private const int MaxFirstPartition = (1 << 19) - 1;

    /// <summary>At most 16 bits per 4 x 4 mode on average (libwebp's <c>max_i4_header_bits</c> at no partition limit).</summary>
    private const int SubblockHeaderBits = 256 * 16 * 16;

    /// <summary>The highest quality at which the chroma DC errors are diffused (libwebp's <c>ERROR_DIFFUSION_QUALITY</c>).</summary>
    private const int ErrorDiffusionQuality = 98;

    /// <summary>Writes the VP8 frame of <paramref name="rgba" /> at <paramref name="quality" /> (0 to 100).</summary>
    public static Vp8EncodedFrame Encode(ReadOnlySpan<byte> rgba, int width, int height, int quality)
    {
        using var picture = new Vp8Picture(rgba, width, height);
        var maxHeaderBits = SubblockHeaderBits;
        while (true)
        {
            var frame = Encode(picture, quality, maxHeaderBits);
            if (frame.FirstPartitionLength <= MaxFirstPartition || maxHeaderBits == 0)
                return frame;

            // Too many 4 x 4 modes for the first partition: allow fewer, as libwebp does, and start over.
            frame.Dispose();
            maxHeaderBits >>= 1;
        }
    }

    private static Vp8EncodedFrame Encode(Vp8Picture picture, int quality, int maxHeaderBits)
    {
        var wide = picture.MacroblocksWide;
        var high = picture.MacroblocksHigh;
        var count = wide * high;
        using var analysis = Vp8Analysis.Run(picture);
        var plan = Vp8SegmentPlan.Build(quality, analysis);

        using var tokens = new Vp8Tokens(count * 25);
        using var modes = new Vp8EncodedModes(count);
        using var costs = new Vp8Costs();
        costs.Update(tokens.Probabilities);
        using (var coder = new Vp8MacroblockCoder(picture, costs, maxHeaderBits, quality <= ErrorDiffusionQuality))
        {
            var decision = new Vp8MacroblockDecision();
            // Refresh the cost tables from the tokens counted so far about eight times per frame.
            var refresh = Math.Max(count >> 3, 96);
            var untilRefresh = refresh;
            for (var mbY = 0; mbY < high; mbY++)
            {
                coder.StartRow();
                for (var mbX = 0; mbX < wide; mbX++)
                {
                    if (--untilRefresh < 0)
                    {
                        tokens.ChooseProbabilities();
                        costs.Update(tokens.Probabilities);
                        untilRefresh = refresh;
                    }

                    var index = mbY * wide + mbX;
                    var segment = analysis.SegmentOfMacroblock[index];
                    coder.Decide(mbX, mbY, plan.Segments[segment], decision);
                    coder.Commit(mbX, decision, tokens);
                    modes.Store(index, segment, decision);
                }

                coder.EndRow();
            }
        }

        tokens.ChooseProbabilities();
        var filterLevel = plan.AdjustFilterStrengths();
        return Vp8EncodedFrame.Write(picture, plan, filterLevel, modes, tokens);
    }
}
