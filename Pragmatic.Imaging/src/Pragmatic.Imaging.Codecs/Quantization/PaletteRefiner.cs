using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Quantization;

/// <summary>
///     Refines a palette by Lloyd's iterations (k-means): every distinct colour of the image goes to its nearest
///     entry, and every entry moves to the mean of the colours it took, weighted by how many pixels have each. A
///     box mean from <see cref="WuQuantizer" /> is the starting point; it stops after the iterations asked for, or
///     when the palette no longer moves.
/// </summary>
/// <remarks>
///     Nearness is squared distance with the channels weighted as luma weighs them (30, 59, 11), as in
///     <see cref="PaletteMapper" />. Against plain RGB distance it gave the GIF encoder, on the reference
///     photographs, a higher luma SSIM and a lower CIEDE2000 than the native library's NeuQuant, where plain
///     distance left both short of it; PSNR, which weighs the channels alike, came out 0.2 to 0.3 dB lower on
///     three of the four.
/// </remarks>
public static class PaletteRefiner
{
    /// <summary>Refines the first <paramref name="entries" /> entries of <paramref name="palette" /> in place, on the opaque pixels of <paramref name="rgba" />.</summary>
    public static void Refine(ReadOnlySpan<byte> rgba, Span<byte> palette, int entries, int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entries, 256);
        var pixels = rgba.Length / 4;
        var capacity = 1;
        while (capacity < pixels * 2)
            capacity <<= 1;

        var keys = ArrayPool<int>.Shared.Rent(capacity);
        var counts = ArrayPool<int>.Shared.Rent(capacity);
        var colours = ArrayPool<int>.Shared.Rent(Math.Max(pixels, 1));
        var weights = ArrayPool<int>.Shared.Rent(Math.Max(pixels, 1));
        try
        {
            var distinct = Distinct(rgba, keys.AsSpan(0, capacity), counts.AsSpan(0, capacity), colours, weights);
            Span<long> sums = stackalloc long[256 * 4];
            Span<byte> order = stackalloc byte[256];
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                sums.Clear();
                var search = new PaletteSearch(palette, entries, skip: -1, order);
                for (var c = 0; c < distinct; c++)
                {
                    var colour = colours[c];
                    int r = (colour >> 16) & 0xFF, g = (colour >> 8) & 0xFF, b = colour & 0xFF;
                    var nearest = search.Nearest(r, g, b);
                    var weight = weights[c];
                    sums[nearest * 4] += (long)r * weight;
                    sums[nearest * 4 + 1] += (long)g * weight;
                    sums[nearest * 4 + 2] += (long)b * weight;
                    sums[nearest * 4 + 3] += weight;
                }

                var moved = false;
                for (var k = 0; k < entries; k++)
                {
                    var weight = sums[k * 4 + 3];
                    if (weight == 0)
                        continue;
                    var r = (byte)((sums[k * 4] + weight / 2) / weight);
                    var g = (byte)((sums[k * 4 + 1] + weight / 2) / weight);
                    var b = (byte)((sums[k * 4 + 2] + weight / 2) / weight);
                    moved |= palette[k * 3] != r || palette[k * 3 + 1] != g || palette[k * 3 + 2] != b;
                    palette[k * 3] = r;
                    palette[k * 3 + 1] = g;
                    palette[k * 3 + 2] = b;
                }

                if (!moved)
                    break;
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(keys);
            ArrayPool<int>.Shared.Return(counts);
            ArrayPool<int>.Shared.Return(colours);
            ArrayPool<int>.Shared.Return(weights);
        }
    }

    // The distinct opaque colours and how many pixels have each, in the order they first appear.
    private static int Distinct(ReadOnlySpan<byte> rgba, Span<int> keys, Span<int> counts, Span<int> colours, Span<int> weights)
    {
        keys.Fill(-1);
        var mask = keys.Length - 1;
        var distinct = 0;
        for (var p = 0; p + 3 < rgba.Length; p += 4)
        {
            if (rgba[p + 3] == 0)
                continue;
            var colour = (rgba[p] << 16) | (rgba[p + 1] << 8) | rgba[p + 2];
            var slot = (int)(((uint)colour * 2654435761u) >> 8) & mask;
            while (keys[slot] != -1 && keys[slot] != colour)
                slot = (slot + 1) & mask;
            if (keys[slot] == -1)
            {
                keys[slot] = colour;
                counts[slot] = distinct;
                colours[distinct] = colour;
                weights[distinct] = 0;
                distinct++;
            }

            weights[counts[slot]]++;
        }

        return distinct;
    }
}
