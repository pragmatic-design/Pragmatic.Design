using System.Buffers;
using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     JPEG to 8-bit RGBA: baseline and extended sequential, and progressive, Huffman-coded, 8-bit,
///     greyscale or YCbCr, with restart intervals. Not: arithmetic coding, 12-bit samples, lossless,
///     hierarchical, CMYK and YCCK (four components).
/// </summary>
public static class JpegDecoder
{
    /// <summary>The image's size from its frame header, without decoding anything else.</summary>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> encoded)
    {
        var segment = FindFrameHeader(encoded, out _);
        if (segment.Length < 5)
            throw new ImageDecodeException("JPEG frame header is too short.");
        return (BinaryPrimitives.ReadUInt16BigEndian(segment[3..]), BinaryPrimitives.ReadUInt16BigEndian(segment[1..]));
    }

    /// <summary>
    ///     Whether <see cref="Decode" /> handles this file's kind of frame: baseline, extended sequential or
    ///     progressive Huffman coding, 8-bit samples, one or three components. Read from the frame header
    ///     alone, so a caller can route the file elsewhere before decoding anything. A file that passes can
    ///     still fail to decode when its data is corrupt.
    /// </summary>
    public static bool CanDecode(ReadOnlySpan<byte> encoded)
    {
        var segment = FindFrameHeader(encoded, out var marker);
        return marker is JpegMarker.Sof0 or JpegMarker.Sof1 or JpegMarker.Sof2
            && segment.Length >= 6
            && segment[0] == 8
            && segment[5] is 1 or 3;
    }

    private static ReadOnlySpan<byte> FindFrameHeader(ReadOnlySpan<byte> encoded, out byte frameMarker)
    {
        if (encoded.Length < 4 || encoded[0] != 0xFF || encoded[1] != JpegMarker.Soi)
            throw new ImageDecodeException("The bytes do not start with a JPEG SOI marker.");

        var position = 2;
        while (true)
        {
            position = NextMarker(encoded, position, out var marker);
            if (marker == JpegMarker.Eoi || marker == JpegMarker.Sos)
                throw new ImageDecodeException("JPEG has no frame header before its scan data.");
            if (marker >= JpegMarker.Rst0 && marker <= JpegMarker.Rst7)
                continue;

            var length = ReadSegmentLength(encoded, position);
            if (marker is >= 0xC0 and <= 0xCF and not JpegMarker.Dht and not 0xC8 and not 0xCC)
            {
                frameMarker = marker;
                return encoded.Slice(position + 2, length - 2);
            }

            position += length;
        }
    }

    /// <summary>
    ///     The largest reduction that still leaves the image at least <paramref name="minWidth" /> ×
    ///     <paramref name="minHeight" />: the decode scale for a thumbnail that will be resized down from there.
    /// </summary>
    public static JpegScale ScaleToCover(int width, int height, int minWidth, int minHeight)
    {
        foreach (var scale in (ReadOnlySpan<JpegScale>)[JpegScale.Eighth, JpegScale.Quarter, JpegScale.Half])
        {
            var d = (int)scale;
            if ((width + d - 1) / d >= minWidth && (height + d - 1) / d >= minHeight)
                return scale;
        }

        return JpegScale.Full;
    }

    /// <summary>Decodes the whole image, at its own size or at a fraction of it (see <see cref="JpegScale" />).</summary>
    public static PixelBuffer Decode(ReadOnlyMemory<byte> encoded, JpegScale scale = JpegScale.Full)
    {
        if (scale is not (JpegScale.Full or JpegScale.Half or JpegScale.Quarter or JpegScale.Eighth))
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale is 1, 1/2, 1/4 or 1/8.");

        var rented = new List<byte[]>();
        var rentedCoefficients = new List<short[]>();
        PixelBuffer? output = null;
        try
        {
            var frame = DecodeFrame(encoded.Span, scale, rented, rentedCoefficients);
            output = new PixelBuffer(frame.OutputWidth, frame.OutputHeight);
            JpegColorConverter.ToRgba(frame, output);
            return output;
        }
        catch
        {
            output?.Dispose();
            throw;
        }
        finally
        {
            Return(rented, rentedCoefficients);
        }
    }

    /// <summary>
    ///     Decodes the image to its components without converting their colour, as zune-jpeg does when its output
    ///     colour space is the input's: the luma of a greyscale image, or Y, Cb and Cr of each pixel, the chroma brought
    ///     up to full size. This is what a JPEG-compressed TIFF strip holds for the TIFF crate.
    /// </summary>
    /// <returns>A pooled array holding width × height × components bytes; the caller returns it to the shared pool.</returns>
    internal static byte[] DecodeComponents(ReadOnlySpan<byte> encoded, out int width, out int height, out int components)
    {
        var rented = new List<byte[]>();
        var rentedCoefficients = new List<short[]>();
        try
        {
            var frame = DecodeFrame(encoded, JpegScale.Full, rented, rentedCoefficients);
            width = frame.OutputWidth;
            height = frame.OutputHeight;
            components = frame.Components.Length;
            var output = ArrayPool<byte>.Shared.Rent(width * height * components);
            JpegColorConverter.ToComponents(frame, output);
            return output;
        }
        finally
        {
            Return(rented, rentedCoefficients);
        }
    }

    private static void Return(List<byte[]> rented, List<short[]> rentedCoefficients)
    {
        foreach (var plane in rented)
            ArrayPool<byte>.Shared.Return(plane);
        foreach (var coefficients in rentedCoefficients)
            ArrayPool<short>.Shared.Return(coefficients);
    }

    private static JpegFrame DecodeFrame(ReadOnlySpan<byte> data, JpegScale scale, List<byte[]> rented, List<short[]> rentedCoefficients)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != JpegMarker.Soi)
            throw new ImageDecodeException("The bytes do not start with a JPEG SOI marker.");

        var quantization = new ushort[4][];
        var dcTables = new JpegHuffmanTable?[4];
        var acTables = new JpegHuffmanTable?[4];
        JpegFrame? frame = null;
        var restartInterval = 0;
        {
            var position = 2;
            while (true)
            {
                position = NextMarker(data, position, out var marker);
                if (marker == JpegMarker.Eoi)
                    break;
                if (marker >= JpegMarker.Rst0 && marker <= JpegMarker.Rst7)
                    continue; // A stray restart marker between segments: nothing to read.

                var length = ReadSegmentLength(data, position);
                var segment = data.Slice(position + 2, length - 2);
                position += length;

                switch (marker)
                {
                    case JpegMarker.Sof0 or JpegMarker.Sof1 or JpegMarker.Sof2:
                        if (frame is not null)
                            throw new ImageDecodeException("JPEG has more than one frame header.");
                        frame = JpegFrame.Parse(marker, segment, scale);
                        AllocatePlanes(frame, rented, rentedCoefficients);
                        break;
                    case >= 0xC3 and <= 0xCF and not JpegMarker.Dht:
                        throw new ImageDecodeException($"JPEG frame type 0x{marker:X2} (lossless, hierarchical or arithmetic-coded) is not supported.");
                    case JpegMarker.Dqt:
                        ReadQuantizationTables(segment, quantization);
                        break;
                    case JpegMarker.Dht:
                        ReadHuffmanTables(segment, dcTables, acTables);
                        break;
                    case JpegMarker.Dri:
                        if (segment.Length < 2)
                            throw new ImageDecodeException("JPEG DRI segment is too short.");
                        restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment);
                        break;
                    case JpegMarker.Sos:
                        if (frame is null)
                            throw new ImageDecodeException("JPEG scan appears before the frame header.");
                        var scan = JpegScan.Parse(segment, frame);
                        var decoder = new JpegScanDecoder(data, position, frame, scan, dcTables, acTables, quantization, restartInterval);
                        position = decoder.Decode();
                        break;
                    // APPn, COM, DNL and the rest: skipped. EXIF orientation is a later concern.
                }
            }

            if (frame is null)
                throw new ImageDecodeException("JPEG has no frame header.");
            if (frame.Progressive)
                TransformProgressive(frame, quantization);
            return frame;
        }
    }

    private static void AllocatePlanes(JpegFrame frame, List<byte[]> rented, List<short[]> rentedCoefficients)
    {
        foreach (var c in frame.Components)
        {
            var plane = ArrayPool<byte>.Shared.Rent(c.PlaneStride * c.PlaneHeight);
            rented.Add(plane);
            c.Plane = plane;
            if (frame.Progressive)
            {
                var count = c.PaddedBlocksPerLine * c.PaddedBlocksPerColumn * 64;
                var coefficients = ArrayPool<short>.Shared.Rent(count);
                Array.Clear(coefficients, 0, count);
                rentedCoefficients.Add(coefficients);
                c.Coefficients = coefficients;
            }
        }
    }

    /// <summary>After the last progressive scan: dequantise every block and transform it into its plane.</summary>
    private static void TransformProgressive(JpegFrame frame, ushort[][] quantization)
    {
        Span<short> block = stackalloc short[64];
        Span<int> natural = stackalloc int[64];
        var zigzag = JpegMarker.Zigzag;
        foreach (var c in frame.Components)
        {
            var q = quantization[c.QuantizationTableId]
                ?? throw new ImageDecodeException($"JPEG component uses quantisation table {c.QuantizationTableId}, which was not defined.");
            // The table is in zigzag order; the coefficients are in natural order.
            for (var k = 0; k < 64; k++)
                natural[zigzag[k]] = q[k];

            for (var row = 0; row < c.PaddedBlocksPerColumn; row++)
            {
                for (var col = 0; col < c.PaddedBlocksPerLine; col++)
                {
                    var source = c.Coefficients.AsSpan((row * c.PaddedBlocksPerLine + col) * 64, 64);
                    for (var i = 0; i < 64; i++)
                        block[i] = (short)(source[i] * natural[i]);
                    var n = c.BlockSize;
                    var plane = c.Plane.AsSpan(row * n * c.PlaneStride + col * n);
                    if (n == 8)
                        JpegIdct.Transform(block, plane, c.PlaneStride);
                    else
                        JpegIdctScaled.Transform(block, n, plane, c.PlaneStride);
                }
            }
        }
    }

    private static void ReadQuantizationTables(ReadOnlySpan<byte> segment, ushort[][] tables)
    {
        var at = 0;
        while (at < segment.Length)
        {
            var precision = segment[at] >> 4;
            var id = segment[at] & 15;
            if (id > 3)
                throw new ImageDecodeException("JPEG quantisation table id is above 3.");
            at++;
            var table = new ushort[64];
            if (precision == 0)
            {
                if (segment.Length - at < 64)
                    throw new ImageDecodeException("JPEG DQT segment is truncated.");
                for (var i = 0; i < 64; i++)
                    table[i] = segment[at + i];
                at += 64;
            }
            else
            {
                if (segment.Length - at < 128)
                    throw new ImageDecodeException("JPEG DQT segment is truncated.");
                for (var i = 0; i < 64; i++)
                    table[i] = BinaryPrimitives.ReadUInt16BigEndian(segment[(at + i * 2)..]);
                at += 128;
            }

            tables[id] = table;
        }
    }

    private static void ReadHuffmanTables(ReadOnlySpan<byte> segment, JpegHuffmanTable?[] dc, JpegHuffmanTable?[] ac)
    {
        var at = 0;
        while (at < segment.Length)
        {
            var kind = segment[at] >> 4;
            var id = segment[at] & 15;
            if (kind > 1 || id > 3)
                throw new ImageDecodeException("JPEG Huffman table class or id is out of range.");
            at++;
            if (segment.Length - at < 16)
                throw new ImageDecodeException("JPEG DHT segment is truncated.");
            var counts = segment.Slice(at, 16);
            var total = 0;
            foreach (var n in counts)
                total += n;
            at += 16;
            if (segment.Length - at < total)
                throw new ImageDecodeException("JPEG DHT segment is truncated.");
            var table = JpegHuffmanTableCache.Get(counts, segment.Slice(at, total));
            at += total;
            if (kind == 0)
                dc[id] = table;
            else
                ac[id] = table;
        }
    }

    /// <summary>Skips to the next marker from <paramref name="position" /> and returns the position after it.</summary>
    private static int NextMarker(ReadOnlySpan<byte> data, int position, out byte marker)
    {
        while (position < data.Length)
        {
            if (data[position] != 0xFF)
            {
                position++;
                continue;
            }

            while (position < data.Length && data[position] == 0xFF)
                position++;
            if (position >= data.Length)
                break;
            marker = data[position];
            if (marker == 0x00)
            {
                position++;
                continue;
            }

            return position + 1;
        }

        throw new ImageDecodeException("JPEG ends without an EOI marker.");
    }

    private static int ReadSegmentLength(ReadOnlySpan<byte> data, int position)
    {
        if (data.Length - position < 2)
            throw new ImageDecodeException("JPEG segment header is truncated.");
        var length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        if (length < 2 || data.Length - position < length)
            throw new ImageDecodeException("JPEG segment length runs past the end of the data.");
        return length;
    }
}
