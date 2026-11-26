using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The colour-indexing side of the lossless encoder (RFC 9649, section 4.4): an image of at most 256
///     colours is written as a table and an index per pixel, several indices to a pixel when there are 16
///     colours or fewer. Finding the colours stops at the 257th, which on a photograph is a few hundred
///     pixels in.
/// </summary>
internal static class Vp8LPalette
{
    public const int MaxColours = 256;
    private const int TableSize = 1024;

    /// <summary>
    ///     The colours of <paramref name="rgba" />, as ARGB in ascending order, written to
    ///     <paramref name="palette" />; returns how many, or 0 when there are more than 256.
    /// </summary>
    public static int Collect(ReadOnlySpan<byte> rgba, Span<uint> palette)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(rgba);
        Span<uint> keys = stackalloc uint[TableSize];
        Span<bool> used = stackalloc bool[TableSize];
        var count = 0;
        var previous = ~pixels[0];
        foreach (var pixel in pixels)
        {
            if (pixel == previous)
                continue;
            previous = pixel;
            var slot = Slot(pixel);
            while (used[slot] && keys[slot] != pixel)
                slot = (slot + 1) & (TableSize - 1);
            if (used[slot])
                continue;
            if (count == MaxColours)
                return 0;
            used[slot] = true;
            keys[slot] = pixel;
            palette[count++] = ToArgb(pixel);
        }

        palette[..count].Sort();
        return count;
    }

    /// <summary>How many indices share a packed pixel, as a power of two, for a table of <paramref name="count" /> colours.</summary>
    public static int PackingBits(int count) => count > 16 ? 0 : count > 4 ? 1 : count > 2 ? 2 : 3;

    /// <summary>
    ///     The index of every pixel, packed into the green of ARGB words, <c>2^bits</c> to a word, the first
    ///     in the lowest bits; alpha is 255 so it codes as a constant.
    /// </summary>
    public static void Bundle(ReadOnlySpan<byte> rgba, int width, int height, ReadOnlySpan<uint> palette, int bits, Span<uint> packed)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(rgba);
        Span<uint> keys = stackalloc uint[TableSize];
        Span<short> indices = stackalloc short[TableSize];
        indices.Fill(-1);
        for (var i = 0; i < palette.Length; i++)
        {
            var colour = ToRgba(palette[i]);
            var slot = Slot(colour);
            while (indices[slot] >= 0)
                slot = (slot + 1) & (TableSize - 1);
            keys[slot] = colour;
            indices[slot] = (short)i;
        }

        var packedWidth = Vp8LInverseTransforms.DivideRoundingUp(width, bits);
        var perWord = 1 << bits;
        var indexBits = 8 >> bits;
        var previous = ~pixels[0];
        var previousIndex = 0;
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width, width);
            var output = packed.Slice(y * packedWidth, packedWidth);
            output.Clear();
            for (var x = 0; x < width; x++)
            {
                var pixel = row[x];
                if (pixel != previous)
                {
                    var slot = Slot(pixel);
                    while (keys[slot] != pixel || indices[slot] < 0)
                        slot = (slot + 1) & (TableSize - 1);
                    previous = pixel;
                    previousIndex = indices[slot];
                }

                output[x >> bits] |= (uint)previousIndex << (8 + (x & (perWord - 1)) * indexBits);
            }

            for (var x = 0; x < packedWidth; x++)
                output[x] |= 0xFF000000;
        }
    }

    private static int Slot(uint colour) => (int)((colour * 0x9E3779B1u) >> 22);

    private static uint ToArgb(uint rgba) => (rgba & 0xFF00FF00) | ((rgba & 0xFF) << 16) | ((rgba >> 16) & 0xFF);

    private static uint ToRgba(uint argb) => (argb & 0xFF00FF00) | ((argb & 0xFF) << 16) | ((argb >> 16) & 0xFF);
}
