using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The optimal code lengths no longer than a limit, by package-merge (Larmore and Hirschberg, 1990):
///     <c>limit</c> lists, each the leaves merged with the pairs of the list before; the first
///     <c>2n - 2</c> items of the last list, expanded, count how deep each leaf goes.
/// </summary>
internal static class Vp8LPackageMerge
{
    /// <param name="weights">The leaves' weights, ascending.</param>
    /// <param name="limit">The longest code allowed; <c>2^limit</c> must be at least the number of leaves.</param>
    /// <param name="lengths">The length of each leaf's code, in the order of <paramref name="weights" />.</param>
    public static void Lengths(ReadOnlySpan<ulong> weights, int limit, Span<byte> lengths)
    {
        var n = weights.Length;
        var width = 2 * n;
        var pool = ArrayPool<ulong>.Shared;
        var itemWeights = pool.Rent(limit * width);
        var itemKinds = ArrayPool<int>.Shared.Rent(limit * width);
        var counts = ArrayPool<int>.Shared.Rent(limit);
        try
        {
            // Level 0: the leaves. A kind of 0 or more is a leaf; -(j + 1) is the pair 2j, 2j + 1 below.
            for (var i = 0; i < n; i++)
            {
                itemWeights[i] = weights[i];
                itemKinds[i] = i;
            }

            counts[0] = n;
            for (var level = 1; level < limit; level++)
            {
                var below = (level - 1) * width;
                var at = level * width;
                var pairs = counts[level - 1] / 2;
                int leaf = 0, pair = 0, count = 0;
                while (leaf < n || pair < pairs)
                {
                    var pairWeight = pair < pairs ? itemWeights[below + 2 * pair] + itemWeights[below + 2 * pair + 1] : ulong.MaxValue;
                    if (leaf < n && weights[leaf] <= pairWeight)
                    {
                        itemWeights[at + count] = weights[leaf];
                        itemKinds[at + count] = leaf++;
                    }
                    else
                    {
                        itemWeights[at + count] = pairWeight;
                        itemKinds[at + count] = -(pair++ + 1);
                    }

                    count++;
                }

                counts[level] = count;
            }

            lengths.Clear();
            var last = limit - 1;
            for (var i = 0; i < 2 * n - 2; i++)
                Expand(itemKinds, width, last, i, lengths);
        }
        finally
        {
            pool.Return(itemWeights);
            ArrayPool<int>.Shared.Return(itemKinds);
            ArrayPool<int>.Shared.Return(counts);
        }
    }

    private static void Expand(int[] kinds, int width, int level, int index, Span<byte> lengths)
    {
        var kind = kinds[level * width + index];
        if (kind >= 0)
        {
            lengths[kind]++;
            return;
        }

        var pair = -kind - 1;
        Expand(kinds, width, level - 1, 2 * pair, lengths);
        Expand(kinds, width, level - 1, 2 * pair + 1, lengths);
    }
}
