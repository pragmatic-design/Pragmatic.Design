using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The RIFF container of a WebP file (Google's "WebP Container Specification"): the simple forms, one
///     <c>VP8 </c> or <c>VP8L</c> chunk, and the extended form, a <c>VP8X</c> header followed by optional
///     chunks, an <c>ALPH</c> chunk before a lossy image, or the frames of an animation. Chunks this decoder
///     has no use for (ICC profile, EXIF, XMP, unknown ones) are stepped over.
/// </summary>
internal static class WebPContainer
{
    private const int RiffHeader = 12;
    private const int ChunkHeader = 8;

    // Chunk tags as the little-endian number their four bytes make.
    private const uint Vp8 = 'V' | ('P' << 8) | ('8' << 16) | ((uint)' ' << 24);
    private const uint Vp8L = 'V' | ('P' << 8) | ('8' << 16) | ((uint)'L' << 24);
    private const uint Vp8X = 'V' | ('P' << 8) | ('8' << 16) | ((uint)'X' << 24);
    private const uint Alph = 'A' | ('L' << 8) | ('P' << 16) | ((uint)'H' << 24);
    private const uint Anmf = 'A' | ('N' << 8) | ('M' << 16) | ((uint)'F' << 24);

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= RiffHeader && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8);

    /// <summary>The canvas size, from the first chunk alone: the bytes may be the start of the file.</summary>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> data)
    {
        var (fourCc, payload, _) = FirstChunk(data);
        if (fourCc == Vp8X)
            return Canvas(data.Slice(payload, Math.Min(10, data.Length - payload)));
        if (fourCc == Vp8L)
        {
            var (width, height, _) = Vp8LDecoder.ReadHeader(data[payload..]);
            return (width, height);
        }

        if (fourCc == Vp8)
            return Vp8Decoder.ReadHeader(data[payload..]);
        throw new ImageDecodeException("WebP: the first chunk is not an image.");
    }

    public static WebPLayout Read(ReadOnlySpan<byte> data)
    {
        var (fourCc, payload, size) = FirstChunk(data);
        if (fourCc == Vp8L)
        {
            var (width, height, alpha) = Vp8LDecoder.ReadHeader(data.Slice(payload, size));
            return new WebPLayout(width, height, alpha, false, 0, 0, (payload, size), true, (0, 0));
        }

        if (fourCc == Vp8)
        {
            var (width, height) = Vp8Decoder.ReadHeader(data.Slice(payload, size));
            return new WebPLayout(width, height, false, false, 0, 0, (payload, size), false, (0, 0));
        }

        if (fourCc != Vp8X)
            throw new ImageDecodeException("WebP: the first chunk is not an image.");
        if (size < 10)
            throw new ImageDecodeException("WebP: the extended header is truncated.");
        var flags = data[payload];
        var (canvasWidth, canvasHeight) = Canvas(data.Slice(payload, size));
        var hasAlpha = (flags & 0x10) != 0;
        var animated = (flags & 0x02) != 0;
        var end = End(data);
        var position = payload + Padded(size);
        if (!animated)
        {
            var image = Image(data, position, end, out var lossless, out var alpha);
            return Checked(new WebPLayout(canvasWidth, canvasHeight, hasAlpha, false, 0, 0, image, lossless, alpha), data);
        }

        while (position + ChunkHeader <= end)
        {
            var (chunk, chunkPayload, chunkSize) = Chunk(data, position, end);
            if (chunk == Anmf)
                return Frame(data, chunkPayload, chunkSize, canvasWidth, canvasHeight, hasAlpha);
            position = chunkPayload + Padded(chunkSize);
        }

        throw new ImageDecodeException("WebP: the animation has no frame.");
    }

    /// <summary>The first frame of an animation: its offset and size on the canvas, then its image chunks.</summary>
    private static WebPLayout Frame(ReadOnlySpan<byte> data, int payload, int size, int canvasWidth, int canvasHeight, bool hasAlpha)
    {
        if (size < 16)
            throw new ImageDecodeException("WebP: an animation frame header is truncated.");
        var frame = data.Slice(payload, 16);
        var x = 2 * UInt24(frame);
        var y = 2 * UInt24(frame[3..]);
        var width = 1 + UInt24(frame[6..]);
        var height = 1 + UInt24(frame[9..]);
        if (x + width > canvasWidth || y + height > canvasHeight)
            throw new ImageDecodeException("WebP: an animation frame lies outside the canvas.");

        var image = Image(data, payload + 16, payload + size, out var lossless, out var alpha);
        var layout = new WebPLayout(canvasWidth, canvasHeight, hasAlpha, true, x, y, image, lossless, alpha);
        var (frameWidth, frameHeight) = BitstreamSize(layout, data);
        if (frameWidth != width || frameHeight != height)
            throw new ImageDecodeException("WebP: an animation frame's image is not the size its header gives.");
        return layout;
    }

    /// <summary>From <paramref name="position" />: an optional <c>ALPH</c> chunk, unknown ones, then the image.</summary>
    private static (int Offset, int Length) Image(ReadOnlySpan<byte> data, int position, int end, out bool lossless, out (int Offset, int Length) alpha)
    {
        alpha = (0, 0);
        while (position + ChunkHeader <= end)
        {
            var (chunk, payload, size) = Chunk(data, position, end);
            if (chunk == Alph)
            {
                if (alpha.Length == 0)
                    alpha = (payload, size);
            }
            else if (IsImage(chunk))
            {
                lossless = chunk == Vp8L;
                if (lossless)
                    alpha = (0, 0);
                return (payload, size);
            }

            position = payload + Padded(size);
        }

        throw new ImageDecodeException("WebP: no image chunk where one is expected.");
    }

    private static WebPLayout Checked(WebPLayout layout, ReadOnlySpan<byte> data)
    {
        if (BitstreamSize(layout, data) != (layout.CanvasWidth, layout.CanvasHeight))
            throw new ImageDecodeException("WebP: the image is not the size of the canvas.");
        return layout;
    }

    private static (int Width, int Height) BitstreamSize(WebPLayout layout, ReadOnlySpan<byte> data)
    {
        var image = data.Slice(layout.Image.Offset, layout.Image.Length);
        if (!layout.IsLossless)
            return Vp8Decoder.ReadHeader(image);
        var (width, height, _) = Vp8LDecoder.ReadHeader(image);
        return (width, height);
    }

    private static (uint FourCc, int Payload, int Size) FirstChunk(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
            throw new ImageDecodeException("WebP: not a RIFF/WEBP file.");
        if (data.Length < RiffHeader + ChunkHeader)
            throw new ImageDecodeException("WebP: the file ends before its first chunk.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(RiffHeader + 4)..]);
        var available = data.Length - RiffHeader - ChunkHeader;
        return (Tag(data[RiffHeader..]), RiffHeader + ChunkHeader, (int)Math.Min(size, (uint)available));
    }

    private static (uint FourCc, int Payload, int Size) Chunk(ReadOnlySpan<byte> data, int position, int end)
    {
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
        var payload = position + ChunkHeader;
        if (size > (uint)(end - payload))
            throw new ImageDecodeException("WebP: a chunk runs past the end of the file.");
        return (Tag(data[position..]), payload, (int)size);
    }

    private static uint Tag(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);

    /// <summary>The end of the RIFF payload, or of the bytes there are if the file is shorter than it says.</summary>
    private static int End(ReadOnlySpan<byte> data)
    {
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        return (int)Math.Min(8u + riffSize, (uint)data.Length);
    }

    private static (int Width, int Height) Canvas(ReadOnlySpan<byte> header)
    {
        if (header.Length < 10)
            throw new ImageDecodeException("WebP: the extended header is truncated.");
        return (1 + UInt24(header[4..]), 1 + UInt24(header[7..]));
    }

    private static bool IsImage(uint fourCc) => fourCc == Vp8 || fourCc == Vp8L;

    private static int Padded(int size) => size + (size & 1);

    private static int UInt24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}
