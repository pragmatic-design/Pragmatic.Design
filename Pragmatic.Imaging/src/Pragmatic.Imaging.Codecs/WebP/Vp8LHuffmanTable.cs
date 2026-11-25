namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     One canonical prefix code of the lossless format, as a two-level lookup table: eight bits index
///     the root, and a longer code continues in a second-level table the root entry points at. Codes are
///     read least significant bit first, so the table is indexed by the bit-reversed code, the way zlib
///     and libwebp build theirs.
/// </summary>
/// <remarks>
///     An entry packs the number of bits in its upper 16 bits and the symbol (or, for a root entry that
///     continues, the offset of its second-level table) in the lower 16. A code with a single symbol takes
///     no bits at all, as the format defines.
/// </remarks>
internal sealed class Vp8LHuffmanTable
{
    private const int RootBits = 8;
    private const int MaxLength = 15;

    private readonly int[] _entries;

    private Vp8LHuffmanTable(int[] entries) => _entries = entries;

    /// <summary>The symbol at the reader's position, its bits consumed.</summary>
    public int ReadSymbol(Vp8LBitReader reader)
    {
        var bits = reader.Peek();
        var entry = _entries[bits & ((1 << RootBits) - 1)];
        var length = entry >> 16;
        if (length > RootBits)
        {
            reader.Skip(RootBits);
            entry = _entries[(entry & 0xFFFF) + (int)((bits >> RootBits) & ((1u << (length - RootBits)) - 1))];
            length = entry >> 16;
        }

        reader.Skip(length);
        return entry & 0xFFFF;
    }

    /// <summary>Whether every symbol of the code is <paramref name="symbol" />: the code then takes no bits.</summary>
    public bool IsSingle(out int symbol)
    {
        var entry = _entries[0];
        symbol = entry & 0xFFFF;
        return entry >> 16 == 0;
    }

    /// <summary>The code whose symbol <c>s</c> has length <c>lengths[s]</c> (0 for a symbol not used).</summary>
    /// <exception cref="ImageDecodeException">The lengths do not describe a complete prefix code.</exception>
    public static Vp8LHuffmanTable Build(ReadOnlySpan<int> lengths)
    {
        Span<int> count = stackalloc int[MaxLength + 1];
        var used = 0;
        var only = 0;
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            var length = lengths[symbol];
            if (length == 0)
                continue;
            if (length > MaxLength)
                throw new ImageDecodeException("WebP: a prefix code length is over 15.");
            count[length]++;
            used++;
            only = symbol;
        }

        if (used == 0)
            throw new ImageDecodeException("WebP: a prefix code has no symbol.");
        if (used == 1)
            return new Vp8LHuffmanTable(Filled(1 << RootBits, only));

        // Kraft: the code must be neither over-subscribed nor incomplete.
        var left = 1;
        for (var length = 1; length <= MaxLength; length++)
        {
            left = (left << 1) - count[length];
            if (left < 0)
                throw new ImageDecodeException("WebP: a prefix code is over-subscribed.");
        }

        if (left != 0)
            throw new ImageDecodeException("WebP: a prefix code is incomplete.");

        // The symbols in canonical order: by length, then by value.
        Span<int> offset = stackalloc int[MaxLength + 2];
        for (var length = 1; length <= MaxLength; length++)
            offset[length + 1] = offset[length] + count[length];
        var sorted = new int[used];
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            if (lengths[symbol] != 0)
                sorted[offset[lengths[symbol]]++] = symbol;
        }

        return new Vp8LHuffmanTable(Fill(sorted, count));
    }

    private static int[] Filled(int size, int symbol)
    {
        var entries = new int[size];
        Array.Fill(entries, symbol);
        return entries;
    }

    private static int[] Fill(int[] sorted, Span<int> count)
    {
        var entries = new List<int>(new int[1 << RootBits]);
        var key = 0;
        var next = 0;

        // Codes that fit the root: each fills every root slot whose low bits are its reversed code.
        for (int length = 1, step = 2; length <= RootBits; length++, step <<= 1)
        {
            for (; count[length] > 0; count[length]--)
            {
                Replicate(entries, key, step, 1 << RootBits, (length << 16) | sorted[next++]);
                key = NextKey(key, length);
            }
        }

        // Longer codes: a second-level table for each root slot they share.
        const int mask = (1 << RootBits) - 1;
        var low = -1;
        var tableStart = 0;
        var tableSize = 0;
        for (int length = RootBits + 1, step = 2; length <= MaxLength; length++, step <<= 1)
        {
            for (; count[length] > 0; count[length]--)
            {
                if ((key & mask) != low)
                {
                    var tableBits = TableBits(count, length);
                    tableSize = 1 << tableBits;
                    tableStart = entries.Count;
                    entries.AddRange(new int[tableSize]);
                    low = key & mask;
                    entries[low] = ((tableBits + RootBits) << 16) | tableStart;
                }

                Replicate(entries, tableStart + (key >> RootBits), step, tableSize, ((length - RootBits) << 16) | sorted[next++]);
                key = NextKey(key, length);
            }
        }

        return [.. entries];
    }

    /// <summary>Writes <paramref name="entry" /> at <paramref name="start" /> and every <paramref name="step" /> after it, up to <paramref name="end" />.</summary>
    private static void Replicate(List<int> entries, int start, int step, int end, int entry)
    {
        for (var i = end - step; i >= 0; i -= step)
            entries[start + i] = entry;
    }

    /// <summary>The bit-reversed increment of <paramref name="key" />, a code of <paramref name="length" /> bits.</summary>
    private static int NextKey(int key, int length)
    {
        var step = 1 << (length - 1);
        while ((key & step) != 0)
            step >>= 1;
        return step != 0 ? (key & (step - 1)) + step : key;
    }

    /// <summary>The bits a second-level table needs to hold the codes from <paramref name="length" /> on that share its root slot.</summary>
    private static int TableBits(Span<int> count, int length)
    {
        var left = 1 << (length - RootBits);
        while (length < MaxLength)
        {
            left -= count[length];
            if (left <= 0)
                break;
            length++;
            left <<= 1;
        }

        return length - RootBits;
    }
}
