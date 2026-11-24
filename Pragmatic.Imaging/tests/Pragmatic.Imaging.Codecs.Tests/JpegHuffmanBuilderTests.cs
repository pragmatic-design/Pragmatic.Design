using Pragmatic.Imaging.Codecs.Jpeg;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     The heap-based builder against the scan Annex K.2 literally describes (reproduced here as the
///     reference): the merge order is a total order, so the two must give the same code lengths.
/// </summary>
public class JpegHuffmanBuilderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    public void Heap_builder_matches_the_literal_k2_scan(int seed)
    {
        var random = new Random(seed);
        var frequencies = new long[257];
        var builder = new JpegHuffmanBuilder();
        // A skewed distribution with ties: a few hundred draws over a Zipf-like range, plus some
        // symbols at exactly equal counts.
        var used = random.Next(20, 200);
        for (var s = 0; s < used; s++)
        {
            var count = Math.Max(1, 5000 / (random.Next(1, 60) * (s % 7 + 1)));
            frequencies[s] = count;
            for (var i = 0; i < count; i++)
                builder.Count(s);
        }

        var ours = builder.Build();
        var (counts, symbols) = ReferenceK2(frequencies);

        Assert.Equal(counts, ours.Counts);
        Assert.Equal(symbols, ours.Symbols);
    }

    [Fact]
    public void A_single_symbol_still_gets_a_code()
    {
        var builder = new JpegHuffmanBuilder();
        for (var i = 0; i < 100; i++)
            builder.Count(0x03);

        var table = builder.Build();

        Assert.Equal(1, table.Lengths[0x03]);
        Assert.Equal(1, table.Counts.Sum(c => (int)c));
    }

    /// <summary>K.2 as written: Code_size, Count_BITS, Adjust_BITS, Sort_input, with the all-ones code reserved.</summary>
    private static (byte[] Counts, byte[] Symbols) ReferenceK2(long[] frequencies)
    {
        var freq = (long[])frequencies.Clone();
        freq[256] = 1;
        var codeSize = new int[257];
        var others = new int[257];
        Array.Fill(others, -1);

        while (true)
        {
            var v1 = -1;
            var v2 = -1;
            for (var s = 0; s < 257; s++)
            {
                if (freq[s] <= 0)
                    continue;
                if (v1 < 0 || freq[s] < freq[v1] || (freq[s] == freq[v1] && s > v1))
                {
                    v2 = v1;
                    v1 = s;
                }
                else if (v2 < 0 || freq[s] < freq[v2] || (freq[s] == freq[v2] && s > v2))
                {
                    v2 = s;
                }
            }

            if (v2 < 0)
                break;

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
        }

        var bits = new int[33];
        for (var i = 0; i < 257; i++)
        {
            if (codeSize[i] > 0)
                bits[Math.Min(codeSize[i], 32)]++;
        }

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
        bits[last]--;

        var counts = new byte[16];
        for (var i = 1; i <= 16; i++)
            counts[i - 1] = (byte)bits[i];

        var symbols = new List<byte>();
        for (var length = 1; length <= 32; length++)
        {
            for (var s = 0; s < 256; s++)
            {
                if (codeSize[s] == length)
                    symbols.Add((byte)s);
            }
        }

        return (counts, symbols.ToArray());
    }
}
