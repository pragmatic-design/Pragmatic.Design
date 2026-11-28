using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Quantization;

/// <summary>
///     Maps RGBA pixels to the entries of a palette: each to the nearest entry, by squared distance with the
///     channels weighted as luma weighs them (30, 59, 11), or, with
///     Floyd-Steinberg dithering, each to the nearest entry of its colour plus the error its left and upper
///     neighbours pushed onto it (7/16 right, 3/16 below left, 5/16 below, 1/16 below right). A pixel whose alpha
///     is zero takes the transparent index, and pushes no error.
/// </summary>
public static class PaletteMapper
{
    private const int CacheSize = 1 << 15;

    /// <param name="rgba">The pixels, row by row.</param>
    /// <param name="width">Pixels per row.</param>
    /// <param name="palette">The entries as R, G, B.</param>
    /// <param name="entries">How many entries the palette has, from 1 to 256.</param>
    /// <param name="transparent">The index a transparent pixel takes; it is not a candidate for the others.</param>
    /// <param name="dither">Whether to diffuse the error.</param>
    /// <param name="indices">One index per pixel.</param>
    public static void Map(ReadOnlySpan<byte> rgba, int width, ReadOnlySpan<byte> palette, int entries, int transparent, bool dither,
        Span<byte> indices)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(entries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entries, 256);
        var pixels = rgba.Length / 4;
        if (indices.Length < pixels)
            throw new ArgumentException("There is an index for fewer pixels than the image has.", nameof(indices));

        var cacheKeys = ArrayPool<int>.Shared.Rent(CacheSize);
        var cacheValues = ArrayPool<byte>.Shared.Rent(CacheSize);
        var errors = dither ? ArrayPool<int>.Shared.Rent((width + 2) * 3 * 2) : null;
        try
        {
            cacheKeys.AsSpan(0, CacheSize).Fill(-1);
            Span<byte> order = stackalloc byte[256];
            var nearest = new Nearest(palette, new PaletteSearch(palette, entries, transparent, order), cacheKeys, cacheValues);
            if (dither)
                Dither(rgba, width, nearest, transparent, errors!.AsSpan(0, (width + 2) * 3 * 2), indices);
            else
                Plain(rgba, nearest, transparent, indices);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(cacheKeys);
            ArrayPool<byte>.Shared.Return(cacheValues);
            if (errors is not null)
                ArrayPool<int>.Shared.Return(errors);
        }
    }

    private static void Plain(ReadOnlySpan<byte> rgba, in Nearest nearest, int transparent, Span<byte> indices)
    {
        for (var i = 0; i < indices.Length && i * 4 + 3 < rgba.Length; i++)
        {
            var p = i * 4;
            indices[i] = rgba[p + 3] == 0 ? (byte)transparent : nearest.Of(rgba[p], rgba[p + 1], rgba[p + 2]);
        }
    }

    private static void Dither(ReadOnlySpan<byte> rgba, int width, in Nearest nearest, int transparent, Span<int> errors, Span<byte> indices)
    {
        // Two rows of error, each with a column of room on both sides: this row's, and the next one's.
        var stride = (width + 2) * 3;
        errors.Clear();
        var height = rgba.Length / 4 / width;
        for (var y = 0; y < height; y++)
        {
            var current = errors.Slice((y & 1) * stride, stride);
            var next = errors.Slice(((y + 1) & 1) * stride, stride);
            next.Clear();
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var p = i * 4;
                if (rgba[p + 3] == 0)
                {
                    indices[i] = (byte)transparent;
                    continue;
                }

                var e = (x + 1) * 3;
                var r = Clamp(rgba[p] + ((current[e] + 8) >> 4));
                var g = Clamp(rgba[p + 1] + ((current[e + 1] + 8) >> 4));
                var b = Clamp(rgba[p + 2] + ((current[e + 2] + 8) >> 4));
                var index = nearest.Of((byte)r, (byte)g, (byte)b);
                indices[i] = index;
                var er = r - nearest.Palette[index * 3];
                var eg = g - nearest.Palette[index * 3 + 1];
                var eb = b - nearest.Palette[index * 3 + 2];
                Spread(current, e + 3, er, eg, eb, 7);
                Spread(next, e - 3, er, eg, eb, 3);
                Spread(next, e, er, eg, eb, 5);
                Spread(next, e + 3, er, eg, eb, 1);
            }
        }
    }

    private static void Spread(Span<int> row, int at, int r, int g, int b, int weight)
    {
        row[at] += r * weight;
        row[at + 1] += g * weight;
        row[at + 2] += b * weight;
    }

    private static int Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    /// <summary>The nearest entry of a colour, remembered in a direct-mapped cache keyed by the colour itself.</summary>
    private readonly ref struct Nearest(ReadOnlySpan<byte> palette, PaletteSearch search, Span<int> keys, Span<byte> values)
    {
        public readonly ReadOnlySpan<byte> Palette = palette;
        private readonly PaletteSearch _search = search;
        private readonly Span<int> _keys = keys;
        private readonly Span<byte> _values = values;

        public byte Of(byte r, byte g, byte b)
        {
            var colour = (r << 16) | (g << 8) | b;
            var slot = (int)(((uint)colour * 2654435761u) >> 17);
            if (_keys[slot] == colour)
                return _values[slot];

            var best = (byte)_search.Nearest(r, g, b);
            _keys[slot] = colour;
            _values[slot] = best;
            return best;
        }
    }
}
