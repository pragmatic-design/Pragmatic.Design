using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Encodes RGBA to WebP: lossless (VP8L), where every pixel decodes back exactly, transparent ones included
///     with their colour; or lossy (VP8) at a quality setting, with any alpha kept exactly beside it.
/// </summary>
public static class WebPEncoder
{
    private const int HeaderSize = 20;
    private const int ChunkHeaderSize = 8;
    private const int ExtendedChunkSize = 10;

    /// <summary>Writes <paramref name="image" /> to <paramref name="output" /> as a lossless WebP file.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The image is wider or taller than 16,384 pixels, the format's limit.</exception>
    public static void EncodeLossless(PixelBuffer image, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var writer = Encode(image);
        var payload = writer.Written;
        Span<byte> header = stackalloc byte[HeaderSize];
        WriteHeader(header, payload.Length);
        output.Write(header);
        output.Write(payload);
        if ((payload.Length & 1) == 1)
            output.WriteByte(0);
    }

    /// <summary>The lossless WebP file of <paramref name="image" />, in an array of exactly its size.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The image is wider or taller than 16,384 pixels, the format's limit.</exception>
    public static byte[] EncodeLossless(PixelBuffer image)
    {
        using var writer = Encode(image);
        var payload = writer.Written;
        var file = new byte[HeaderSize + payload.Length + (payload.Length & 1)];
        WriteHeader(file, payload.Length);
        payload.CopyTo(file.AsSpan(HeaderSize));
        return file;
    }

    /// <summary>
    ///     Writes <paramref name="image" /> to <paramref name="output" /> as a lossy WebP file at
    ///     <paramref name="quality" />, 0 to 100 as libwebp reads it. An image with any pixel not opaque gets its
    ///     alpha in an <c>ALPH</c> chunk, losslessly.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The image is wider or taller than 16,383 pixels, the format's limit, or the quality is outside 0 to 100.
    /// </exception>
    public static void EncodeLossy(PixelBuffer image, Stream output, int quality)
    {
        ArgumentNullException.ThrowIfNull(output);
        var file = EncodeLossy(image, quality);
        output.Write(file);
    }

    /// <summary>The lossy WebP file of <paramref name="image" /> at <paramref name="quality" />, in an array of exactly its size.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The image is wider or taller than 16,383 pixels, the format's limit, or the quality is outside 0 to 100.
    /// </exception>
    public static byte[] EncodeLossy(PixelBuffer image, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(image.Width, 16383, nameof(image));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(image.Height, 16383, nameof(image));
        ArgumentOutOfRangeException.ThrowIfNegative(quality);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);

        using var frame = Vp8Encoder.Encode(image.Pixels, image.Width, image.Height, quality);
        var frameChunk = ChunkHeaderSize + Padded(frame.Length);
        if (!WebPAlphaEncoder.HasAlpha(image.Pixels))
        {
            var simple = new byte[12 + frameChunk];
            WriteRiff(simple, simple.Length);
            WriteChunk(simple.AsSpan(12), "VP8 "u8, frame.Length);
            frame.CopyTo(simple.AsSpan(12 + ChunkHeaderSize));
            return simple;
        }

        using var alpha = WebPAlphaEncoder.Encode(image);
        var alphaLength = alpha.Written.Length;
        var alphaChunk = ChunkHeaderSize + Padded(alphaLength);
        var file = new byte[12 + ChunkHeaderSize + ExtendedChunkSize + alphaChunk + frameChunk];
        WriteRiff(file, file.Length);
        var at = 12;
        WriteChunk(file.AsSpan(at), "VP8X"u8, ExtendedChunkSize);
        file[at + ChunkHeaderSize] = 0x10; // the alpha flag
        WriteUInt24(file.AsSpan(at + ChunkHeaderSize + 4), image.Width - 1);
        WriteUInt24(file.AsSpan(at + ChunkHeaderSize + 7), image.Height - 1);
        at += ChunkHeaderSize + ExtendedChunkSize;
        WriteChunk(file.AsSpan(at), "ALPH"u8, alphaLength);
        alpha.Written.CopyTo(file.AsSpan(at + ChunkHeaderSize));
        at += alphaChunk;
        WriteChunk(file.AsSpan(at), "VP8 "u8, frame.Length);
        frame.CopyTo(file.AsSpan(at + ChunkHeaderSize));
        return file;
    }

    private static int Padded(int length) => length + (length & 1);

    private static void WriteRiff(Span<byte> file, int length)
    {
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file[4..], (uint)(length - 8));
        "WEBP"u8.CopyTo(file[8..]);
    }

    private static void WriteChunk(Span<byte> at, ReadOnlySpan<byte> fourCc, int length)
    {
        fourCc.CopyTo(at);
        BinaryPrimitives.WriteUInt32LittleEndian(at[4..], (uint)length);
    }

    private static void WriteUInt24(Span<byte> at, int value)
    {
        at[0] = (byte)value;
        at[1] = (byte)(value >> 8);
        at[2] = (byte)(value >> 16);
    }

    private static Vp8LBitWriter Encode(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(image.Width, 16384, nameof(image));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(image.Height, 16384, nameof(image));

        var writer = new Vp8LBitWriter(image.ByteLength / 2);
        try
        {
            Vp8LEncoder.Encode(image, writer);
            writer.Flush();
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    private static void WriteHeader(Span<byte> header, int payloadLength)
    {
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)(4 + 8 + payloadLength + (payloadLength & 1)));
        "WEBPVP8L"u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)payloadLength);
    }
}
