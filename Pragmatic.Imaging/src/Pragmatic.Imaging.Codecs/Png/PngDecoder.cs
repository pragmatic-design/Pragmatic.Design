using System.Buffers;
using System.IO.Compression;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     PNG to 8-bit RGBA: every colour type, every bit depth, interlaced or not. Rows are inflated,
///     unfiltered and converted one at a time, so what the decode holds beyond the pixels is two rows.
/// </summary>
public static class PngDecoder
{
    /// <summary>
    ///     The image's size from its IHDR chunk, without inflating anything: what a caller checks against
    ///     its limits before <see cref="Decode" /> allocates the pixels. Only the signature and the first
    ///     chunk are read, so the beginning of a file is enough.
    /// </summary>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> encoded)
    {
        var reader = new PngChunkReader(encoded);
        if (!reader.TryNext(out var first) || first.Type != PngChunk.Ihdr)
            throw new ImageDecodeException("PNG does not start with IHDR.");

        var header = PngHeader.Parse(encoded.Slice(first.DataOffset, first.DataLength));
        return (header.Width, header.Height);
    }

    /// <summary>Decodes the whole image. The caller owns the buffer.</summary>
    public static PixelBuffer Decode(ReadOnlyMemory<byte> encoded)
    {
        var (header, palette, idat) = ReadChunks(encoded.Span);
        if (idat.Count == 0)
            throw new ImageDecodeException("PNG has no IDAT chunk.");
        if (header.ColorType == PngColorType.Palette && palette.Count == 0)
            throw new ImageDecodeException("PNG is palette-based and has no PLTE chunk.");

        var image = new PixelBuffer(header.Width, header.Height);
        try
        {
            using var inflater = new ZLibStream(new PngIdatStream(encoded, idat), CompressionMode.Decompress);
            if (header.Interlaced)
                DecodeInterlaced(inflater, header, palette, image);
            else
                DecodeRows(inflater, header, palette, image);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static (PngHeader Header, PngPalette Palette, List<PngChunk> Idat) ReadChunks(ReadOnlySpan<byte> data)
    {
        var reader = new PngChunkReader(data);
        if (!reader.TryNext(out var first) || first.Type != PngChunk.Ihdr)
            throw new ImageDecodeException("PNG does not start with IHDR.");

        var header = PngHeader.Parse(data.Slice(first.DataOffset, first.DataLength));
        var palette = new PngPalette();
        var idat = new List<PngChunk>();
        while (reader.TryNext(out var chunk))
        {
            switch (chunk.Type)
            {
                case PngChunk.Plte:
                    palette.ReadPlte(data.Slice(chunk.DataOffset, chunk.DataLength));
                    break;
                case PngChunk.Trns:
                    palette.ReadTrns(header.ColorType, data.Slice(chunk.DataOffset, chunk.DataLength));
                    break;
                case PngChunk.Idat:
                    idat.Add(chunk);
                    break;
                // Every other chunk (text, gamma, colour profile, timing, ...) carries nothing a pixel needs.
            }
        }

        return (header, palette, idat);
    }

    private const int StagingSize = 256 * 1024;

    /// <summary>
    ///     Rows one at a time, but read from the inflater in large pieces: a read costs a lock and a
    ///     buffer round in the runtime's stream, so a staging buffer holds many filtered rows and each
    ///     row is unfiltered in place inside it, against the previous row, which is still there.
    /// </summary>
    private static void DecodeRows(Stream inflater, in PngHeader header, PngPalette palette, PixelBuffer image)
    {
        var rowBytes = header.ScanlineBytes(header.Width);
        var filteredRowBytes = rowBytes + 1;
        var step = header.FilterStep;
        var pool = ArrayPool<byte>.Shared;
        var stagingLength = Math.Max(StagingSize, filteredRowBytes * 2);
        var staging = pool.Rent(stagingLength);
        var priorFallback = pool.Rent(rowBytes);
        try
        {
            Array.Clear(priorFallback, 0, rowBytes);
            var priorSpan = priorFallback.AsMemory(0, rowBytes);
            var filled = 0;
            var consumed = 0;
            for (var y = 0; y < header.Height; y++)
            {
                if (filled - consumed < filteredRowBytes)
                {
                    // Move to the start what must survive the refill: the previous row (unfiltered, for the
                    // filters that look up) and the bytes of the next row already read, then read on after them.
                    var tail = filled - consumed;
                    var keep = y == 0 ? 0 : rowBytes;
                    staging.AsSpan(consumed - keep, keep + tail).CopyTo(staging);
                    filled = keep + tail + ReadAtLeast(inflater, staging.AsSpan(keep + tail, stagingLength - keep - tail), filteredRowBytes - tail);
                    consumed = keep;
                    if (keep > 0)
                        priorSpan = staging.AsMemory(0, rowBytes);
                }

                var filter = staging[consumed];
                var row = staging.AsSpan(consumed + 1, rowBytes);
                PngFilter.Unfilter(filter, row, priorSpan.Span, step);
                PngScanlineConverter.Convert(header, palette, row, image.Row(y), header.Width);
                priorSpan = staging.AsMemory(consumed + 1, rowBytes);
                consumed += filteredRowBytes;
            }
        }
        finally
        {
            pool.Return(staging);
            pool.Return(priorFallback);
        }
    }

    /// <summary>Fills <paramref name="buffer" /> from the inflater until at least <paramref name="minimum" /> bytes are in, or the data ends.</summary>
    private static int ReadAtLeast(Stream inflater, Span<byte> buffer, int minimum)
    {
        var total = 0;
        try
        {
            while (total < buffer.Length)
            {
                var read = inflater.Read(buffer[total..]);
                if (read == 0)
                    break;
                total += read;
                if (total >= minimum && read < 4096)
                    break;
            }
        }
        catch (InvalidDataException e)
        {
            throw new ImageDecodeException("PNG image data is not a valid zlib stream.", e);
        }

        if (total < minimum)
            throw new ImageDecodeException("PNG image data ends before its last row.");
        return total;
    }

    private static void DecodeInterlaced(Stream inflater, in PngHeader header, PngPalette palette, PixelBuffer image)
    {
        var pool = ArrayPool<byte>.Shared;
        var step = header.FilterStep;
        foreach (var pass in PngInterlace.Passes)
        {
            var passWidth = pass.PassWidth(header.Width);
            var passHeight = pass.PassHeight(header.Height);
            if (passWidth == 0 || passHeight == 0)
                continue;

            var rowBytes = header.ScanlineBytes(passWidth);
            var prior = pool.Rent(rowBytes);
            var current = pool.Rent(rowBytes);
            var rgba = pool.Rent(passWidth * 4);
            try
            {
                Array.Clear(prior, 0, rowBytes);
                for (var py = 0; py < passHeight; py++)
                {
                    var filter = ReadFilterByte(inflater);
                    ReadRow(inflater, current.AsSpan(0, rowBytes));
                    PngFilter.Unfilter(filter, current.AsSpan(0, rowBytes), prior.AsSpan(0, rowBytes), step);
                    PngScanlineConverter.Convert(header, palette, current.AsSpan(0, rowBytes), rgba, passWidth);

                    var row = image.Row(pass.YStart + py * pass.YStep);
                    for (int px = 0, x = pass.XStart; px < passWidth; px++, x += pass.XStep)
                        rgba.AsSpan(px * 4, 4).CopyTo(row.Slice(x * 4, 4));

                    (prior, current) = (current, prior);
                }
            }
            finally
            {
                pool.Return(prior);
                pool.Return(current);
                pool.Return(rgba);
            }
        }
    }

    private static byte ReadFilterByte(Stream inflater)
    {
        var filter = inflater.ReadByte();
        if (filter < 0)
            throw new ImageDecodeException("PNG image data ends before its last row.");
        return (byte)filter;
    }

    private static void ReadRow(Stream inflater, Span<byte> row)
    {
        try
        {
            inflater.ReadExactly(row);
        }
        catch (EndOfStreamException e)
        {
            throw new ImageDecodeException("PNG image data ends before its last row.", e);
        }
        catch (InvalidDataException e)
        {
            throw new ImageDecodeException("PNG image data is not a valid zlib stream.", e);
        }
    }
}
