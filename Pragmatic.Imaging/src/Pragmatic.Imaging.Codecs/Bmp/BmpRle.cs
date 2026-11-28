namespace Pragmatic.Imaging.Codecs.Bmp;

/// <summary>
///     RLE8 and RLE4 pixel data (BMP compression 1 and 2), decoded the way the image crate 0.25 decodes it:
///     what the runs do not reach is black, a delta leaves black behind it, an RLE8 run too long for its row
///     stops at the row's end, and an RLE4 run or an absolute run that does not fit is an error.
/// </summary>
internal static class BmpRle
{
    /// <param name="data">The whole file.</param>
    /// <param name="header">Its headers; the image is bottom-up, as compressed BMPs always are.</param>
    /// <param name="palette">The palette as R, G, B, 256 entries.</param>
    /// <param name="rgba">The image, already black and opaque.</param>
    public static void Decode(ReadOnlySpan<byte> data, in BmpHeader header, ReadOnlySpan<byte> palette, Span<byte> rgba)
    {
        var width = header.Width;
        var height = header.Height;
        var four = header.Layout == BmpLayout.Rle4;
        var position = header.DataOffset;

        // A stored row, the bottom one first, and the next pixel of it the decode writes; x is where the
        // stream says it is, which a clamped RLE8 run leaves past the row's end.
        var row = 0;
        while (row < height)
        {
            var pixel = 0;
            var x = 0;
            while (true)
            {
                int control = Next(data, ref position);
                if (control != 0)
                {
                    int index = Next(data, ref position);
                    int written;
                    if (four)
                    {
                        if (pixel + control > width)
                            throw Corrupt();
                        for (var i = 0; i < control; i++)
                            Put(rgba, width, height, row, pixel + i, palette, (i & 1) == 0 ? index >> 4 : index & 15);
                        written = control;
                    }
                    else
                    {
                        written = Math.Min(control, width - pixel);
                        for (var i = 0; i < written; i++)
                            Put(rgba, width, height, row, pixel + i, palette, index);
                    }

                    pixel += written;
                    x += control;
                    continue;
                }

                int op = Next(data, ref position);
                if (op == 0)
                    break;
                if (op == 1)
                    return;
                if (op == 2)
                {
                    int dx = Next(data, ref position);
                    int dy = Next(data, ref position);
                    if (dy > 0)
                    {
                        // The rest of this row, any row between and the start of the new one up to x stay black.
                        if (row + dy >= height || x > width)
                            throw Corrupt();
                        row += dy;
                        pixel = x;
                    }

                    if (pixel + dx > width)
                        throw Corrupt();
                    pixel += dx;
                    x += dx;
                    continue;
                }

                // Absolute: op indices, padded to a whole number of 16-bit words.
                var bytes = four ? (op + 1) / 2 : op;
                bytes += bytes & 1;
                if (position + bytes > data.Length)
                    throw Truncated();
                if (pixel + op > width)
                    throw Corrupt();
                var run = data.Slice(position, bytes);
                for (var i = 0; i < op; i++)
                {
                    var index = four ? (i & 1) == 0 ? run[i >> 1] >> 4 : run[i >> 1] & 15 : run[i];
                    Put(rgba, width, height, row, pixel + i, palette, index);
                }

                position += bytes;
                pixel += op;
                x += op;
            }

            row++;
        }
    }

    private static int Next(ReadOnlySpan<byte> data, ref int position)
    {
        if ((uint)position >= (uint)data.Length)
            throw Truncated();
        return data[position++];
    }

    private static void Put(Span<byte> rgba, int width, int height, int row, int x, ReadOnlySpan<byte> palette, int index)
    {
        var at = ((height - 1 - row) * width + x) * 4;
        rgba[at] = palette[index * 3];
        rgba[at + 1] = palette[index * 3 + 1];
        rgba[at + 2] = palette[index * 3 + 2];
    }

    private static ImageDecodeException Corrupt() => new("BMP: the run-length data is corrupt.");

    private static ImageDecodeException Truncated() => new("BMP: the run-length data is truncated.");
}
