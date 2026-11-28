namespace Pragmatic.Imaging.Codecs.Quantization;

/// <summary>
///     The nearest entry of a palette to a colour, by squared distance with the channels weighted as luma weighs
///     them (30, 59, 11). Exact, and quicker than trying every entry: the entries are sorted by green, the channel
///     that weighs most, and the search goes out from the closest green both ways, stopping on each side once the
///     green difference alone costs more than the best entry found.
/// </summary>
internal readonly ref struct PaletteSearch
{
    public const int WeightRed = 30;
    public const int WeightGreen = 59;
    public const int WeightBlue = 11;

    private readonly ReadOnlySpan<byte> _palette;
    private readonly Span<byte> _order;
    private readonly int _count;

    /// <param name="palette">The entries as R, G, B.</param>
    /// <param name="entries">How many there are.</param>
    /// <param name="skip">An entry that is never the answer (a transparent one), or -1.</param>
    /// <param name="order">Scratch of 256 bytes for the sorted order.</param>
    public PaletteSearch(ReadOnlySpan<byte> palette, int entries, int skip, Span<byte> order)
    {
        _palette = palette;
        _order = order;
        var count = 0;
        for (var k = 0; k < entries; k++)
        {
            if (k != skip)
                order[count++] = (byte)k;
        }

        // Insertion sort by green: at most 256 entries, sorted once.
        for (var i = 1; i < count; i++)
        {
            var entry = order[i];
            var green = palette[entry * 3 + 1];
            var j = i - 1;
            while (j >= 0 && palette[order[j] * 3 + 1] > green)
            {
                order[j + 1] = order[j];
                j--;
            }

            order[j + 1] = entry;
        }

        _count = count;
    }

    public static int Distance(int dr, int dg, int db) => WeightRed * dr * dr + WeightGreen * dg * dg + WeightBlue * db * db;

    public int Nearest(int r, int g, int b)
    {
        // The first entry whose green is not below the colour's.
        int low = 0, high = _count;
        while (low < high)
        {
            var middle = (low + high) >> 1;
            if (_palette[_order[middle] * 3 + 1] < g)
                low = middle + 1;
            else
                high = middle;
        }

        var best = _order[Math.Min(low, _count - 1)];
        var bestDistance = int.MaxValue;
        int up = low, down = low - 1;
        while (up < _count || down >= 0)
        {
            if (up < _count)
            {
                var entry = _order[up];
                var dg = _palette[entry * 3 + 1] - g;
                if (WeightGreen * dg * dg > bestDistance)
                {
                    up = _count;
                }
                else
                {
                    var distance = Distance(_palette[entry * 3] - r, dg, _palette[entry * 3 + 2] - b);
                    if (distance < bestDistance || (distance == bestDistance && entry < best))
                    {
                        bestDistance = distance;
                        best = entry;
                    }

                    up++;
                }
            }

            if (down >= 0)
            {
                var entry = _order[down];
                var dg = _palette[entry * 3 + 1] - g;
                if (WeightGreen * dg * dg > bestDistance)
                {
                    down = -1;
                }
                else
                {
                    var distance = Distance(_palette[entry * 3] - r, dg, _palette[entry * 3 + 2] - b);
                    if (distance < bestDistance || (distance == bestDistance && entry < best))
                    {
                        bestDistance = distance;
                        best = entry;
                    }

                    down--;
                }
            }
        }

        return best;
    }
}
