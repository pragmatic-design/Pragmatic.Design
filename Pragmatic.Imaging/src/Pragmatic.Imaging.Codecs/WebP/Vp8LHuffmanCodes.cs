// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The canonical prefix codes of one lossless stream, every one in a single pooled block, as libwebp
///     keeps them: each code is a two-level lookup table at an offset in the block. Eight bits index the
///     root; a longer code continues in a second-level table the root entry points at. Codes are read
///     least significant bit first, so the tables are indexed by the bit-reversed code, the way zlib and
///     libwebp build theirs.
/// </summary>
/// <remarks>
///     An entry packs the number of bits in its low 5 bits and, above them, the symbol (or, for a root
///     entry that continues, the absolute offset of its second-level table). A code with a single symbol
///     takes no bits, as the format defines.
/// </remarks>
internal sealed class Vp8LHuffmanCodes : IDisposable
{
    public const int RootBits = 8;
    private const int RootSize = 1 << RootBits;
    private const int MaxLength = 15;
    private const int LengthBits = 5;
    private const int LengthMask = (1 << LengthBits) - 1;

    private int[] _entries = [];
    private int _used;

    /// <summary>Starts an empty set of codes, with a block from the pool sized for <paramref name="expectedCodes" />.</summary>
    public void Rent(int expectedCodes)
    {
        _entries = ArrayPool<int>.Shared.Rent(Math.Max(1, expectedCodes) * RootSize * 2);
        _used = 0;
    }

    /// <summary>The block every code's table lives in; a code is its root offset in it.</summary>
    public int[] Entries => _entries;

    /// <summary>Gives the block back; a second call, or one before <see cref="Rent" />, does nothing.</summary>
    public void Dispose()
    {
        if (_entries.Length == 0)
            return;
        ArrayPool<int>.Shared.Return(_entries);
        _entries = [];
    }

    /// <summary>The symbol at the reader's position, its bits consumed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadSymbol(int[] entries, int root, Vp8LBitReader reader)
    {
        var bits = reader.Peek();
        var entry = entries[root + (int)(bits & (RootSize - 1))];
        var length = entry & LengthMask;
        if (length > RootBits)
        {
            reader.Skip(RootBits);
            entry = entries[(entry >> LengthBits) + (int)((bits >> RootBits) & ((1u << (length - RootBits)) - 1))];
            length = entry & LengthMask;
        }

        reader.Skip(length);
        return entry >> LengthBits;
    }

    /// <summary>The one symbol of the code at <paramref name="root" />, which then takes no bits, or -1 if it has more.</summary>
    public static int SingleSymbol(int[] entries, int root)
    {
        var entry = entries[root];
        return (entry & LengthMask) == 0 ? entry >> LengthBits : -1;
    }

    /// <summary>
    ///     Adds the code whose symbol <c>s</c> has length <c>lengths[s]</c> (0 for a symbol not used) and
    ///     returns its root offset. <paramref name="sorted" /> is scratch of at least as many entries.
    /// </summary>
    /// <exception cref="ImageDecodeException">The lengths do not describe a complete prefix code.</exception>
    public int Add(ReadOnlySpan<int> lengths, Span<int> sorted)
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

        var root = Allocate(RootSize);
        if (used == 1)
        {
            _entries.AsSpan(root, RootSize).Fill(only << LengthBits);
            return root;
        }

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
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            if (lengths[symbol] != 0)
                sorted[offset[lengths[symbol]]++] = symbol;
        }

        Fill(root, sorted[..used], count);
        return root;
    }

    private void Fill(int root, ReadOnlySpan<int> sorted, Span<int> count)
    {
        var key = 0;
        var next = 0;

        // Codes that fit the root: each fills every root slot whose low bits are its reversed code.
        for (int length = 1, step = 2; length <= RootBits; length++, step <<= 1)
        {
            for (; count[length] > 0; count[length]--)
            {
                Replicate(root + key, step, RootSize, (sorted[next++] << LengthBits) | length);
                key = NextKey(key, length);
            }
        }

        // Longer codes: a second-level table for each root slot they share.
        const int mask = RootSize - 1;
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
                    tableStart = Allocate(tableSize);
                    low = key & mask;
                    _entries[root + low] = (tableStart << LengthBits) | (tableBits + RootBits);
                }

                Replicate(tableStart + (key >> RootBits), step, tableSize, (sorted[next++] << LengthBits) | (length - RootBits));
                key = NextKey(key, length);
            }
        }
    }

    /// <summary>Space for <paramref name="size" /> entries at the end of the block, which grows when full.</summary>
    private int Allocate(int size)
    {
        if (_used + size > _entries.Length)
        {
            var larger = ArrayPool<int>.Shared.Rent(Math.Max(_entries.Length * 2, _used + size));
            _entries.AsSpan(0, _used).CopyTo(larger);
            ArrayPool<int>.Shared.Return(_entries);
            _entries = larger;
        }

        var start = _used;
        _used += size;
        return start;
    }

    /// <summary>Writes <paramref name="entry" /> at <paramref name="start" /> and every <paramref name="step" /> after it, up to <paramref name="end" />.</summary>
    private void Replicate(int start, int step, int end, int entry)
    {
        for (var i = end - step; i >= 0; i -= step)
            _entries[start + i] = entry;
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
