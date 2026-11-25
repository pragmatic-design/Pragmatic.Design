using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The <c>ALPH</c> chunk beside a lossy frame (WebP container specification, "Alpha"): one byte of
///     header, then the alpha plane raw or as the green channel of a header-less lossless stream, then
///     undone from the prediction filter it was stored with. Level pre-processing needs nothing to decode:
///     libwebp smooths such a plane only when dithering is asked for, and it is not by default.
/// </summary>
internal static class WebPAlpha
{
    /// <summary>Writes the alpha of each pixel into the fourth byte of <paramref name="rgba" />.</summary>
    public static void Apply(ReadOnlySpan<byte> chunk, int width, int height, Span<byte> rgba)
    {
        if (chunk.Length < 1)
            throw new ImageDecodeException("WebP: the alpha chunk is empty.");
        var header = chunk[0];
        var compression = header & 3;
        var filter = (header >> 2) & 3;
        if (compression > 1 || ((header >> 4) & 3) > 1)
            throw new ImageDecodeException("WebP: the alpha chunk has an unknown compression or pre-processing.");

        var count = width * height;
        var plane = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            if (compression == 0)
            {
                if (chunk.Length - 1 < count)
                    throw new ImageDecodeException("WebP: the raw alpha plane is truncated.");
                chunk.Slice(1, count).CopyTo(plane);
            }
            else
            {
                var argb = ArrayPool<uint>.Shared.Rent(count);
                try
                {
                    Vp8LDecoder.DecodeHeaderless(chunk[1..], width, height, argb);
                    for (var i = 0; i < count; i++)
                        plane[i] = (byte)(argb[i] >> 8);
                }
                finally
                {
                    ArrayPool<uint>.Shared.Return(argb);
                }
            }

            Unfilter(plane, width, height, filter);
            for (var i = 0; i < count; i++)
                rgba[i * 4 + 3] = plane[i];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plane);
        }
    }

    /// <summary>
    ///     Undoes the filter in place. The first row is always predicted from the left (and its first pixel
    ///     from nothing); the first pixel of every other row from the one above (libwebp's <c>filters.c</c>).
    /// </summary>
    private static void Unfilter(byte[] plane, int width, int height, int filter)
    {
        if (filter == 0)
            return;

        for (var x = 1; x < width; x++)
            plane[x] += plane[x - 1];

        for (var y = 1; y < height; y++)
        {
            var row = y * width;
            var above = row - width;
            plane[row] += plane[above];
            for (var x = 1; x < width; x++)
            {
                var prediction = filter switch
                {
                    1 => plane[row + x - 1],
                    2 => plane[above + x],
                    _ => Gradient(plane[row + x - 1], plane[above + x], plane[above + x - 1]),
                };
                plane[row + x] += prediction;
            }
        }
    }

    private static byte Gradient(int left, int top, int topLeft) => (byte)Math.Clamp(left + top - topLeft, 0, 255);
}
