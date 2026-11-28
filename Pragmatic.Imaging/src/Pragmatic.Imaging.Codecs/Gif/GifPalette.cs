using Pragmatic.Imaging.Codecs.Quantization;

namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     The colour table and the indices of a frame. As the gif crate does for the native library, alpha counts as
///     none or full, and every transparent pixel is the first transparent one's colour; at most 256 such colours
///     make an exact table, ordered by red, green, blue and alpha, the colour of the transparent pixels among them.
///     More colours take Wu's palette, of 255 entries and a transparent one when the image has transparency.
/// </summary>
internal static class GifPalette
{
    private const int Slots = 1024;

    // Lloyd's iterations over Wu's box means; with them the palette holds NeuQuant's quality on the reference
    // photographs (TheGifEncoderHoldsItsQualityTests).
    private const int RefineIterations = 8;

    /// <returns>The entries written to <paramref name="palette" />, and the transparent index or -1.</returns>
    public static (int Entries, int Transparent) Build(ReadOnlySpan<byte> rgba, int width, bool dither, Span<byte> palette, Span<byte> indices)
    {
        // The first transparent pixel's colour, which every transparent pixel takes.
        var transparentKey = -1L;
        for (var p = 0; p < rgba.Length; p += 4)
        {
            if (rgba[p + 3] == 0)
            {
                transparentKey = Key(rgba[p], rgba[p + 1], rgba[p + 2], 0);
                break;
            }
        }

        Span<uint> keys = stackalloc uint[257];
        var distinct = Distinct(rgba, transparentKey, keys);
        if (distinct <= 256)
            return Exact(rgba, keys[..distinct], transparentKey, palette, indices);

        var transparent = transparentKey >= 0;
        var entries = WuQuantizer.Quantize(rgba, transparent ? 255 : 256, palette);
        PaletteRefiner.Refine(rgba, palette, entries, RefineIterations);
        var transparentIndex = -1;
        if (transparent)
        {
            transparentIndex = entries;
            palette[entries * 3] = (byte)(transparentKey >> 24);
            palette[entries * 3 + 1] = (byte)(transparentKey >> 16);
            palette[entries * 3 + 2] = (byte)(transparentKey >> 8);
            entries++;
        }

        PaletteMapper.Map(rgba, width, palette, entries, transparentIndex, dither, indices);
        return (entries, transparentIndex);
    }

    private static uint Key(byte r, byte g, byte b, byte a) => ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;

    // The pixel's colour as the gif crate sees it: opaque, or the first transparent pixel's colour.
    private static uint KeyOf(ReadOnlySpan<byte> rgba, int p, long transparentKey) =>
        rgba[p + 3] == 0 ? (uint)transparentKey : Key(rgba[p], rgba[p + 1], rgba[p + 2], 0xFF);

    // Up to 257 distinct colours, in an open-addressed set; 257 means there are more than 256.
    private static int Distinct(ReadOnlySpan<byte> rgba, long transparentKey, Span<uint> found)
    {
        Span<uint> set = stackalloc uint[Slots];
        Span<bool> used = stackalloc bool[Slots];
        var count = 0;
        for (var p = 0; p < rgba.Length; p += 4)
        {
            var key = KeyOf(rgba, p, transparentKey);
            var slot = (int)((key * 2654435761u) >> 22);
            while (used[slot] && set[slot] != key)
                slot = (slot + 1) & (Slots - 1);
            if (used[slot])
                continue;
            used[slot] = true;
            set[slot] = key;
            found[count++] = key;
            if (count > 256)
                return count;
        }

        return count;
    }

    private static (int Entries, int Transparent) Exact(ReadOnlySpan<byte> rgba, Span<uint> keys, long transparentKey, Span<byte> palette,
        Span<byte> indices)
    {
        keys.Sort();
        for (var k = 0; k < keys.Length; k++)
        {
            palette[k * 3] = (byte)(keys[k] >> 24);
            palette[k * 3 + 1] = (byte)(keys[k] >> 16);
            palette[k * 3 + 2] = (byte)(keys[k] >> 8);
        }

        for (int i = 0, p = 0; p < rgba.Length; i++, p += 4)
            indices[i] = (byte)keys.BinarySearch(KeyOf(rgba, p, transparentKey));
        var transparent = transparentKey >= 0 ? keys.BinarySearch((uint)transparentKey) : -1;
        return (keys.Length, transparent);
    }
}
