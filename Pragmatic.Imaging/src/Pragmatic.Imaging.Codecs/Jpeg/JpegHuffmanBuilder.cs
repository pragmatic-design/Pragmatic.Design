namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     A Huffman table fitted to the symbols a file actually uses: the procedure of Annex K.2, which
///     limits code lengths to 16 bits and reserves the all-ones code. What it produces is what DHT
///     carries (counts per length, symbols in code order) and what the encoder looks codes up in.
/// </summary>
internal sealed class JpegHuffmanBuilder
{
    private readonly long[] _frequencies = new long[257];

    public void Count(int symbol) => _frequencies[symbol]++;

    public JpegEncodingTable Build()
    {
        Span<long> freq = stackalloc long[257];
        _frequencies.CopyTo(freq);
        freq[256] = 1; // Reserved: keeps any real symbol off the all-ones code.

        Span<int> codeSize = stackalloc int[257];
        Span<int> others = stackalloc int[257];
        others.Fill(-1);

        // Only the symbols with a frequency take part in the merging. K.2 takes, at every step, the
        // two least frequent, the larger symbol value first on a tie: that is a total order, so a heap
        // keyed by it merges in the same sequence as the scan the standard describes.
        Span<int> heap = stackalloc int[257];
        var heapCount = 0;
        for (var i = 0; i < 257; i++)
        {
            if (freq[i] > 0)
                heap[heapCount++] = i;
        }

        for (var i = heapCount / 2 - 1; i >= 0; i--)
            SiftDown(heap, heapCount, i, freq);

        while (heapCount > 1)
        {
            var v1 = heap[0];
            heap[0] = heap[--heapCount];
            SiftDown(heap, heapCount, 0, freq);
            var v2 = heap[0];

            freq[v1] += freq[v2];
            freq[v2] = 0;

            codeSize[v1]++;
            var chain = v1;
            while (others[chain] >= 0)
            {
                chain = others[chain];
                codeSize[chain]++;
            }

            others[chain] = v2;
            codeSize[v2]++;
            chain = v2;
            while (others[chain] >= 0)
            {
                chain = others[chain];
                codeSize[chain]++;
            }

            // v2 is spent; v1, heavier now, takes its place at the top and sinks to where it belongs.
            heap[0] = v1;
            SiftDown(heap, heapCount, 0, freq);
        }

        // A code deeper than 32 bits takes frequencies that grow like Fibonacci numbers over 257 symbols;
        // clamped, it is folded by the adjustment below like any other over-long code.
        Span<int> bits = stackalloc int[33];
        for (var i = 0; i < 257; i++)
        {
            if (codeSize[i] > 0)
            {
                codeSize[i] = Math.Min(codeSize[i], 32);
                bits[codeSize[i]]++;
            }
        }

        // Fold codes longer than 16 bits into shorter ones (K.2, "Adjust_BITS").
        for (var i = 32; i > 16; i--)
        {
            while (bits[i] > 0)
            {
                var j = i - 2;
                while (bits[j] == 0)
                    j--;
                bits[i] -= 2;
                bits[i - 1]++;
                bits[j + 1] += 2;
                bits[j]--;
            }
        }

        var last = 16;
        while (bits[last] == 0)
            last--;
        bits[last]--; // The reserved symbol's code is dropped.

        var counts = new byte[16];
        for (var i = 1; i <= 16; i++)
            counts[i - 1] = (byte)bits[i];

        // Symbols in order of code length, then value; the reserved one excluded.
        var total = 0;
        for (var i = 0; i < 16; i++)
            total += counts[i];
        var symbols = new byte[total];
        var next = 0;
        for (var length = 1; length <= 32; length++)
        {
            for (var s = 0; s < 256; s++)
            {
                if (codeSize[s] == length)
                    symbols[next++] = (byte)s;
            }
        }

        return JpegEncodingTable.FromCanonical(counts, symbols);
    }

    /// <summary>Lower frequency first; on a tie the larger symbol value, as K.2's "V1"/"V2" selection says.</summary>
    private static bool Precedes(int a, int b, ReadOnlySpan<long> freq) => freq[a] < freq[b] || (freq[a] == freq[b] && a > b);

    private static void SiftDown(Span<int> heap, int count, int at, ReadOnlySpan<long> freq)
    {
        var item = heap[at];
        while (true)
        {
            var child = 2 * at + 1;
            if (child >= count)
                break;
            if (child + 1 < count && Precedes(heap[child + 1], heap[child], freq))
                child++;
            if (!Precedes(heap[child], item, freq))
                break;
            heap[at] = heap[child];
            at = child;
        }

        heap[at] = item;
    }
}
