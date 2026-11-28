using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     Decodes a GIF's first frame to RGBA on its logical screen, the picture the native library returned (the
///     image crate 0.25 over the gif crate 0.14): the frame at its offset, clipped to the screen, transparent
///     black around it; its transparent index as that colour with alpha 0; an index past the colour table
///     transparent black. Interlaced frames are put back in row order. Later frames are not read.
/// </summary>
public static class GifDecoder
{
    /// <summary>Whether the bytes start like a GIF file.</summary>
    public static bool IsGif(ReadOnlySpan<byte> encoded) => GifHeader.HasSignature(encoded);

    /// <summary>The size of the image, its logical screen, from the first 13 bytes.</summary>
    /// <exception cref="ImageDecodeException">The bytes are not a GIF header.</exception>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> encoded) => GifHeader.Identify(encoded);

    /// <exception cref="ImageDecodeException">The bytes are truncated, corrupt, or not a GIF this decoder reads.</exception>
    public static PixelBuffer Decode(ReadOnlySpan<byte> encoded)
    {
        var header = GifHeader.Read(encoded);
        if (header.Width == 0 || header.Height == 0)
            throw new ImageDecodeException("GIF: the logical screen has no pixels.");

        // A frame with no pixels reads no data; the gif crate refuses it unless it is interlaced and has no rows.
        var pixels = header.FrameWidth * header.FrameHeight;
        if (pixels == 0 && !(header.Interlaced && header.FrameHeight == 0))
            throw new ImageDecodeException("GIF: the frame has no pixels.");

        var image = new PixelBuffer(header.Width, header.Height);
        var indices = ArrayPool<byte>.Shared.Rent(Math.Max(pixels, 1));
        var table = ArrayPool<byte>.Shared.Rent(GifLzwDecoder.TableBytes);
        try
        {
            image.Pixels.Clear();
            if (pixels > 0)
            {
                var decoder = new GifLzwDecoder(new GifSubBlocks(encoded, header.DataStart), header.MinimumCodeSize, table);
                decoder.Decode(indices.AsSpan(0, pixels));
                Compose(header, encoded.Slice(header.PaletteStart, header.PaletteLength), indices, image);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(indices);
            ArrayPool<byte>.Shared.Return(table);
        }
    }

    private static void Compose(in GifHeader header, ReadOnlySpan<byte> palette, byte[] indices, PixelBuffer image)
    {
        var width = header.FrameWidth;
        var height = header.FrameHeight;
        var right = Math.Min(header.FrameLeft + width, header.Width);
        var columns = right - header.FrameLeft;

        // Interlaced rows come every 8th from 0, every 8th from 4, every 4th from 2, then every 2nd from 1.
        var row = 0;
        var pass = 0;
        for (var stored = 0; stored < height; stored++)
        {
            var y = header.FrameTop + row;
            if (columns > 0 && y < header.Height)
            {
                var source = indices.AsSpan(stored * width, columns);
                var target = image.Row(y).Slice(header.FrameLeft * 4, columns * 4);
                for (var x = 0; x < columns; x++)
                {
                    int index = source[x];
                    if (index * 3 + 3 > palette.Length)
                        continue;
                    target[x * 4] = palette[index * 3];
                    target[x * 4 + 1] = palette[index * 3 + 1];
                    target[x * 4 + 2] = palette[index * 3 + 2];
                    target[x * 4 + 3] = index == header.Transparent ? (byte)0 : (byte)0xFF;
                }
            }

            if (!header.Interlaced)
            {
                row++;
                continue;
            }

            row += pass switch { 0 or 1 => 8, 2 => 4, _ => 2 };
            while (row >= height && pass < 3)
            {
                pass++;
                row = pass switch { 1 => 4, 2 => 2, _ => 1 };
            }
        }
    }
}
