namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The path through the 4 x 4 intra mode tree (<see cref="Vp8Tables.BModeTree" />) to each mode: the
///     node probabilities it passes, and the bit it takes at each. The encoder writes a mode along it, and
///     costs a mode by it.
/// </summary>
internal static class Vp8SubblockModeTree
{
    private const int MaxDepth = 9;

    // Per mode: its depth, then pairs of (probability index, bit).
    private static readonly byte[] Paths = BuildPaths();

    /// <summary>Writes <paramref name="mode" /> with the nine probabilities of its context.</summary>
    public static void Write(Vp8BoolEncoder output, ReadOnlySpan<byte> probabilities, int mode)
    {
        var path = Paths.AsSpan(mode * (1 + 2 * MaxDepth));
        for (var i = 0; i < path[0]; i++)
            output.PutBit(path[2 + 2 * i], probabilities[path[1 + 2 * i]]);
    }

    /// <summary>The cost of <paramref name="mode" /> with the nine probabilities of its context.</summary>
    public static int Cost(ReadOnlySpan<byte> probabilities, int mode)
    {
        var path = Paths.AsSpan(mode * (1 + 2 * MaxDepth));
        var cost = 0;
        for (var i = 0; i < path[0]; i++)
            cost += Vp8Costs.Bit(path[2 + 2 * i], probabilities[path[1 + 2 * i]]);
        return cost;
    }

    private static byte[] BuildPaths()
    {
        var paths = new byte[10 * (1 + 2 * MaxDepth)];
        Span<int> nodes = stackalloc int[MaxDepth];
        Span<int> bits = stackalloc int[MaxDepth];
        for (var mode = 0; mode < 10; mode++)
        {
            var depth = Find(Vp8Tables.BModeTree, 0, -mode, nodes, bits, 0);
            var path = paths.AsSpan(mode * (1 + 2 * MaxDepth));
            path[0] = (byte)depth;
            for (var i = 0; i < depth; i++)
            {
                path[1 + 2 * i] = (byte)(nodes[i] >> 1);
                path[2 + 2 * i] = (byte)bits[i];
            }
        }

        return paths;
    }

    /// <summary>Depth-first search for <paramref name="leaf" />; returns its depth, or -1 below <paramref name="node" />.</summary>
    private static int Find(ReadOnlySpan<sbyte> tree, int node, int leaf, Span<int> nodes, Span<int> bits, int depth)
    {
        for (var bit = 0; bit < 2; bit++)
        {
            nodes[depth] = node;
            bits[depth] = bit;
            var next = tree[node + bit];
            if (next <= 0)
            {
                if (next == leaf)
                    return depth + 1;
                continue;
            }

            var found = Find(tree, next, leaf, nodes, bits, depth + 1);
            if (found > 0)
                return found;
        }

        return -1;
    }
}
