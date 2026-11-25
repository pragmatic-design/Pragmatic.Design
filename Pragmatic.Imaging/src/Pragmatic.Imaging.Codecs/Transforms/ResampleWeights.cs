namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     For one axis of a resize, the source samples each destination sample draws on and with what
///     weight: computed once per axis, the same for every row (or column) the pass visits. Weights of
///     one destination index are contiguous in <see cref="Weights" />, normalised to sum to one.
/// </summary>
internal sealed class ResampleWeights
{
    private ResampleWeights(int[] starts, int[] counts, float[] weights, int tapsPerIndex)
    {
        Starts = starts;
        Counts = counts;
        Weights = weights;
        TapsPerIndex = tapsPerIndex;
    }

    /// <summary>First source index of destination index i.</summary>
    public int[] Starts { get; }

    /// <summary>How many source samples destination index i uses.</summary>
    public int[] Counts { get; }

    /// <summary>All weights, <see cref="TapsPerIndex" /> per destination index, zero-padded.</summary>
    public float[] Weights { get; }

    /// <summary>The stride of <see cref="Weights" />: the largest count, so every index starts at i × TapsPerIndex.</summary>
    public int TapsPerIndex { get; }

    public static ResampleWeights Compute(int sourceLength, int destinationLength, in ResampleKernel kernel)
    {
        var scale = (double)sourceLength / destinationLength;
        // Shrinking widens the filter by the scale, so every source sample is heard; growing keeps it.
        var filterScale = Math.Max(scale, 1.0);
        var support = kernel.Support * filterScale;
        var tapsPerIndex = (int)Math.Ceiling(support * 2) + 1;

        var starts = new int[destinationLength];
        var counts = new int[destinationLength];
        var weights = new float[destinationLength * tapsPerIndex];
        Span<double> raw = stackalloc double[tapsPerIndex];

        for (var d = 0; d < destinationLength; d++)
        {
            var center = (d + 0.5) * scale - 0.5;
            var first = Math.Max(0, (int)Math.Floor(center - support));
            var last = Math.Min(sourceLength - 1, (int)Math.Ceiling(center + support));
            if (last < first)
                last = first;

            var count = 0;
            var sum = 0.0;
            for (var s = first; s <= last && count < tapsPerIndex; s++)
            {
                var w = kernel.Weight((s - center) / filterScale);
                raw[count++] = w;
                sum += w;
            }

            // A tap that fell entirely outside the kernel contributes nothing; keep the window compact.
            if (sum == 0)
            {
                raw[0] = 1;
                sum = 1;
                count = 1;
                first = Math.Clamp((int)Math.Round(center), 0, sourceLength - 1);
            }

            starts[d] = first;
            counts[d] = count;
            var offset = d * tapsPerIndex;
            for (var t = 0; t < count; t++)
                weights[offset + t] = (float)(raw[t] / sum);
        }

        return new ResampleWeights(starts, counts, weights, tapsPerIndex);
    }
}
