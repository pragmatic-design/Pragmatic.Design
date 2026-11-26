// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The coefficient tokens of a frame (RFC 6386, section 13), held until the frame's probabilities are
///     known: each block is recorded as its levels, and counted node by node of the token tree; once every
///     macroblock is decided the probabilities that code these counts in fewest bits are chosen, and the
///     blocks are written with them. libwebp's token buffer does the same. An encoder reuses its token store
///     frame after frame: <see cref="Begin" /> empties it, and <see cref="Release" /> gives the storage back.
/// </summary>
internal sealed class Vp8Tokens
{
    private const int ProbabilityCount = Vp8Tables.NumTypes * Vp8Tables.NumBands * Vp8Tables.NumContexts * Vp8Tables.NumProbabilities;

    /// <summary>
    ///     The store comes in pages from the pool, each a whole number of block records (a record is at most
    ///     17 shorts): it grows by a page and never copies, where an array that doubles leaves each size it
    ///     outgrew in the pool, held for nothing.
    /// </summary>
    private const int PageSize = 16 * 1024;

    private readonly int[] _ones = new int[ProbabilityCount];
    private readonly int[] _totals = new int[ProbabilityCount];

    // The full pages and how much of each holds records, then the page being written. The lists stay with
    // the store from frame to frame, so a frame no larger than one before allocates nothing for them.
    private readonly List<short[]> _pages = [];
    private readonly List<int> _pageLengths = [];
    private short[] _page = [];
    private int _length;

    /// <summary>The probabilities the frame codes its tokens with: [type, band, context, node].</summary>
    public byte[] Probabilities { get; } = Vp8Tables.DefaultCoefficientProbabilities.ToArray();

    public static int Offset(int type, int band, int context) =>
        ((type * Vp8Tables.NumBands + band) * Vp8Tables.NumContexts + context) * Vp8Tables.NumProbabilities;

    /// <summary>How many shorts the recorded blocks take: a frame's token partition is about a quarter of it in bytes.</summary>
    public int Stored
    {
        get
        {
            var stored = _length;
            foreach (var length in _pageLengths)
                stored += length;
            return stored;
        }
    }

    /// <summary>Starts a frame: no tokens, no counts, the default probabilities.</summary>
    public void Begin()
    {
        Release();
        _ones.AsSpan().Clear();
        _totals.AsSpan().Clear();
        Vp8Tables.DefaultCoefficientProbabilities.CopyTo(Probabilities);
    }

    /// <summary>Gives the token storage back to the pool.</summary>
    public void Release()
    {
        var pool = ArrayPool<short>.Shared;
        foreach (var page in _pages)
            pool.Return(page);
        _pages.Clear();
        _pageLengths.Clear();
        if (_page.Length > 0)
            pool.Return(_page);
        _page = [];
        _length = 0;
    }

    /// <summary>
    ///     Records one block's levels (scan order) from position <paramref name="first" />, in context
    ///     <paramref name="ctx" />, and returns whether it has any level that is not zero.
    /// </summary>
    public bool Record(int type, int first, int ctx, ReadOnlySpan<short> levels)
    {
        var last = -1;
        for (var n = 15; n >= first; n--)
        {
            if (levels[n] != 0)
            {
                last = n;
                break;
            }
        }

        var count = last < 0 ? 0 : last + 1 - first;
        if (_length + count + 1 > _page.Length)
            NextPage();
        var page = _page;
        page[_length++] = (short)(type | (ctx << 2) | (first << 4) | (count << 5));
        for (var n = first; n <= last; n++)
            page[_length++] = levels[n];

        Count(type, first, ctx, levels, last);
        return last >= 0;
    }

    /// <summary>
    ///     Chooses, for each probability, the default or the one its counts call for, whichever codes the
    ///     counts and the update in fewer bits (libwebp's <c>FinalizeTokenProbas</c>).
    /// </summary>
    public void ChooseProbabilities()
    {
        var defaults = Vp8Tables.DefaultCoefficientProbabilities;
        var update = Vp8Tables.CoefficientUpdateProbabilities;
        for (var i = 0; i < ProbabilityCount; i++)
        {
            var ones = _ones[i];
            var total = _totals[i];
            var old = defaults[i];
            var fitted = ones == 0 ? 255 : 255 - ones * 255 / total;
            if (fitted < 1)
                fitted = 1;
            var oldCost = Branch(ones, total, old) + Vp8Costs.Bit(0, update[i]);
            var newCost = Branch(ones, total, fitted) + Vp8Costs.Bit(1, update[i]) + 8 * 256;
            Probabilities[i] = oldCost > newCost ? (byte)fitted : old;
        }
    }

    /// <summary>Writes which probabilities differ from the defaults, and their values, to the frame header.</summary>
    public void WriteProbabilities(Vp8BoolEncoder header)
    {
        var defaults = Vp8Tables.DefaultCoefficientProbabilities;
        var update = Vp8Tables.CoefficientUpdateProbabilities;
        for (var i = 0; i < ProbabilityCount; i++)
        {
            var changed = Probabilities[i] != defaults[i];
            header.PutBit(changed ? 1 : 0, update[i]);
            if (changed)
                header.PutLiteral(Probabilities[i], 8);
        }
    }

    /// <summary>Writes every recorded block, in order, with <see cref="Probabilities" />.</summary>
    public void Emit(Vp8BoolEncoder output)
    {
        Span<short> levels = stackalloc short[16];
        for (var p = 0; p < _pages.Count; p++)
            Emit(output, _pages[p], _pageLengths[p], levels);
        Emit(output, _page, _length, levels);
    }

    private void Emit(Vp8BoolEncoder output, short[] page, int length, Span<short> levels)
    {
        var i = 0;
        while (i < length)
        {
            var head = page[i++];
            var type = head & 3;
            var ctx = (head >> 2) & 3;
            var first = (head >> 4) & 1;
            var count = (head >> 5) & 31;
            levels.Clear();
            for (var n = 0; n < count; n++)
                levels[first + n] = page[i++];
            Write(output, type, first, ctx, levels, count == 0 ? -1 : first + count - 1);
        }
    }

    private void Write(Vp8BoolEncoder output, int type, int n, int ctx, ReadOnlySpan<short> levels, int last)
    {
        var bands = Vp8Tables.Bands;
        var probabilities = Probabilities;
        var p = Offset(type, bands[n], ctx);
        output.PutBit(last >= 0 ? 1 : 0, probabilities[p]);
        if (last < 0)
            return;

        while (n < 16)
        {
            int c = levels[n++];
            var v = c < 0 ? -c : c;
            output.PutBit(v != 0 ? 1 : 0, probabilities[p + 1]);
            if (v == 0)
            {
                p = Offset(type, bands[n], 0);
                continue;
            }

            if (v == 1)
            {
                output.PutBit(0, probabilities[p + 2]);
                p = Offset(type, bands[n], 1);
            }
            else
            {
                output.PutBit(1, probabilities[p + 2]);
                WriteLarge(output, probabilities, p, v);
                p = Offset(type, bands[n], 2);
            }

            output.PutBitUniform(c < 0 ? 1 : 0);
            if (n == 16)
                return;
            output.PutBit(n <= last ? 1 : 0, probabilities[p]);
            if (n > last)
                return;
        }
    }

    private static void WriteLarge(Vp8BoolEncoder output, byte[] probabilities, int p, int v)
    {
        if (v <= 4)
        {
            output.PutBit(0, probabilities[p + 3]);
            output.PutBit(v != 2 ? 1 : 0, probabilities[p + 4]);
            if (v != 2)
                output.PutBit(v == 4 ? 1 : 0, probabilities[p + 5]);
            return;
        }

        output.PutBit(1, probabilities[p + 3]);
        if (v <= 10)
        {
            output.PutBit(0, probabilities[p + 6]);
            if (v <= 6)
            {
                output.PutBit(0, probabilities[p + 7]);
                output.PutBit(v == 6 ? 1 : 0, 159);
            }
            else
            {
                output.PutBit(1, probabilities[p + 7]);
                output.PutBit(v >= 9 ? 1 : 0, 165);
                output.PutBit((v & 1) == 0 ? 1 : 0, 145);
            }

            return;
        }

        output.PutBit(1, probabilities[p + 6]);
        var extra = Vp8Tables.LargeCategory(v, out var category, out var bits, out var first);
        output.PutBit(category >> 1, probabilities[p + 8]);
        output.PutBit(category & 1, probabilities[p + 9 + (category >> 1)]);
        v -= first;
        for (var i = 0; i < bits; i++)
            output.PutBit((v >> (bits - 1 - i)) & 1, extra[i]);
    }

    /// <summary>Counts each adaptive node the block's tokens pass through, as <see cref="Write" /> would code them.</summary>
    private void Count(int type, int n, int ctx, ReadOnlySpan<short> levels, int last)
    {
        var bands = Vp8Tables.Bands;
        var p = Offset(type, bands[n], ctx);
        Tally(p, last >= 0 ? 1 : 0);
        if (last < 0)
            return;

        while (n < 16)
        {
            int c = levels[n++];
            var v = c < 0 ? -c : c;
            Tally(p + 1, v != 0 ? 1 : 0);
            if (v == 0)
            {
                p = Offset(type, bands[n], 0);
                continue;
            }

            Tally(p + 2, v > 1 ? 1 : 0);
            if (v > 1)
                CountLarge(p, v);
            p = Offset(type, bands[n], v == 1 ? 1 : 2);
            if (n == 16)
                return;
            Tally(p, n <= last ? 1 : 0);
            if (n > last)
                return;
        }
    }

    private void CountLarge(int p, int v)
    {
        if (v <= 4)
        {
            Tally(p + 3, 0);
            Tally(p + 4, v != 2 ? 1 : 0);
            if (v != 2)
                Tally(p + 5, v == 4 ? 1 : 0);
            return;
        }

        Tally(p + 3, 1);
        if (v <= 10)
        {
            Tally(p + 6, 0);
            Tally(p + 7, v > 6 ? 1 : 0);
            return;
        }

        Tally(p + 6, 1);
        if (v < 3 + (8 << 2))
        {
            Tally(p + 8, 0);
            Tally(p + 9, v >= 3 + (8 << 1) ? 1 : 0);
        }
        else
        {
            Tally(p + 8, 1);
            Tally(p + 10, v >= 3 + (8 << 3) ? 1 : 0);
        }
    }

    private void Tally(int index, int bit)
    {
        _ones[index] += bit;
        _totals[index]++;
    }

    private static int Branch(int ones, int total, int probability) =>
        ones * Vp8Costs.Bit(1, probability) + (total - ones) * Vp8Costs.Bit(0, probability);

    /// <summary>Closes the page being written, if any, and starts a new one from the pool.</summary>
    private void NextPage()
    {
        if (_page.Length > 0)
        {
            _pages.Add(_page);
            _pageLengths.Add(_length);
        }

        _page = ArrayPool<short>.Shared.Rent(PageSize);
        _length = 0;
    }
}
