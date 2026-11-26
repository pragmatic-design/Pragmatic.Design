using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Encodes RGBA to WebP. Lossless (VP8L) only, in the simple container: every pixel decodes back
///     exactly, transparent ones included, with their colour.
/// </summary>
public static class WebPEncoder
{
    private const int HeaderSize = 20;

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
