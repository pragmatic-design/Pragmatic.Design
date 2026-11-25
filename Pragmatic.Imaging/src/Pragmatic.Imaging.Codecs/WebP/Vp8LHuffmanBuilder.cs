namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Builds a length-limited canonical prefix code from symbol frequencies, for the lossless encoder.
///     A tree too deep is rebuilt with every frequency raised to a floor that doubles each time, which is
///     libwebp's way (<c>VP8LCreateHuffmanTree</c>). Codes are canonical, by length then symbol, as the
///     decoder assigns them, and stored bit-reversed because the format writes least significant bit first.
/// </summary>
internal static class Vp8LHuffmanBuilder
{
    /// <summary>
    ///     Writes into <paramref name="lengths" /> and <paramref name="codes" /> the code for
    ///     <paramref name="frequencies" />, no code longer than <paramref name="limit" /> bits, and returns
    ///     how many symbols are used. One used symbol gets length 0: it takes no bits.
    /// </summary>
    public static int Build(ReadOnlySpan<uint> frequencies, int limit, Span<byte> lengths, Span<ushort> codes)
    {
        lengths.Clear();
        codes.Clear();
        var n = frequencies.Length;
        var used = 0;
        for (var i = 0; i < n; i++)
        {
            if (frequencies[i] != 0)
                used++;
        }

        if (used <= 1)
            return used;

        // Leaves 0..used-1, sorted by weight then symbol; internal nodes after them. The sort key packs
        // the weight above the symbol, which is below 4,096 in every alphabet of the format.
        Span<ulong> keys = stackalloc ulong[used];
        Span<int> symbols = stackalloc int[used];
        Span<ulong> weights = stackalloc ulong[2 * used];
        Span<int> parents = stackalloc int[2 * used];

        for (ulong floor = 1; ; floor *= 2)
        {
            for (int i = 0, k = 0; i < n; i++)
            {
                if (frequencies[i] != 0)
                    keys[k++] = (Math.Max(frequencies[i], floor) << 12) | (uint)i;
            }

            keys.Sort();
            for (var i = 0; i < used; i++)
            {
                symbols[i] = (int)(keys[i] & 0xFFF);
                weights[i] = keys[i] >> 12;
            }

            // Two queues: the sorted leaves, and the internal nodes, which come out in order of weight.
            int leaf = 0, inner = used, next = used;
            while (next < 2 * used - 1)
            {
                var a = Take(weights, ref leaf, used, ref inner, next);
                var b = Take(weights, ref leaf, used, ref inner, next);
                weights[next] = weights[a] + weights[b];
                parents[a] = parents[b] = next;
                next++;
            }

            var deepest = 0;
            for (var i = 0; i < used; i++)
            {
                var depth = 0;
                for (var node = i; node != 2 * used - 2; node = parents[node])
                    depth++;
                lengths[symbols[i]] = (byte)depth;
                deepest = Math.Max(deepest, depth);
            }

            if (deepest <= limit)
                break;
        }

        AssignCodes(lengths, codes, limit);
        return used;
    }

    private static int Take(Span<ulong> weights, ref int leaf, int leaves, ref int inner, int next)
    {
        if (leaf < leaves && (inner >= next || weights[leaf] <= weights[inner]))
            return leaf++;
        return inner++;
    }

    /// <summary>Canonical codes from lengths, bit-reversed for a least-significant-first writer.</summary>
    public static void AssignCodes(ReadOnlySpan<byte> lengths, Span<ushort> codes, int limit)
    {
        Span<int> count = stackalloc int[16];
        for (var i = 0; i < lengths.Length; i++)
            count[lengths[i]]++;
        count[0] = 0;
        Span<int> next = stackalloc int[16];
        var code = 0;
        for (var length = 1; length <= limit; length++)
        {
            code = (code + count[length - 1]) << 1;
            next[length] = code;
        }

        for (var i = 0; i < lengths.Length; i++)
        {
            var length = lengths[i];
            if (length == 0)
                continue;
            codes[i] = Reverse(next[length]++, length);
        }
    }

    private static ushort Reverse(int code, int length)
    {
        var reversed = 0;
        for (var i = 0; i < length; i++)
        {
            reversed = (reversed << 1) | (code & 1);
            code >>= 1;
        }

        return (ushort)reversed;
    }
}
