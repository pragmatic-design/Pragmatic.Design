using System.Buffers;
using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Bmp;

/// <summary>
///     Writes RGBA as the BMP the native library wrote (the image crate 0.25's encoder, byte for byte): a
///     BITMAPV4HEADER, 32 bits per pixel as bitfields with an alpha mask, the sRGB colour space, rows bottom-up.
/// </summary>
public static class BmpEncoder
{
    private const int FileHeaderSize = 14;
    private const int V4HeaderSize = 108;
    private const int HeadersSize = FileHeaderSize + V4HeaderSize;

    /// <exception cref="ArgumentException">The image is larger than a BMP's 32-bit sizes can describe.</exception>
    public static byte[] Encode(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var output = new byte[checked((int)FileSize(image))];
        Write(image, output);
        return output;
    }

    public static void Write(PixelBuffer image, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        using var lease = new Rgba8Lease(image);
        image = lease.Image;
        Span<byte> headers = stackalloc byte[HeadersSize];
        WriteHeaders(image, headers);
        output.Write(headers);
        var buffer = ArrayPool<byte>.Shared.Rent(image.Width * 4);
        try
        {
            for (var y = image.Height - 1; y >= 0; y--)
            {
                ToBgra(image.Row(y), buffer);
                output.Write(buffer, 0, image.Width * 4);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void Write(PixelBuffer image, Span<byte> output)
    {
        using var lease = new Rgba8Lease(image);
        image = lease.Image;
        WriteHeaders(image, output);
        var stride = image.Width * 4;
        for (var y = image.Height - 1; y >= 0; y--)
            ToBgra(image.Row(y), output.Slice(HeadersSize + (image.Height - 1 - y) * stride, stride));
    }

    private static long FileSize(PixelBuffer image)
    {
        var size = HeadersSize + (long)image.Width * 4 * image.Height;
        if (size > uint.MaxValue)
            throw new ArgumentException($"A {image.Width} x {image.Height} image is larger than a BMP can describe.", nameof(image));
        return size;
    }

    private static void WriteHeaders(PixelBuffer image, Span<byte> headers)
    {
        var imageSize = (uint)((long)image.Width * 4 * image.Height);
        headers.Clear();
        headers[0] = (byte)'B';
        headers[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(headers[2..], (uint)FileSize(image));
        BinaryPrimitives.WriteUInt32LittleEndian(headers[10..], HeadersSize);
        var dib = headers[FileHeaderSize..];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, V4HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib[4..], image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(dib[8..], image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib[14..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dib[16..], 3); // bitfields
        BinaryPrimitives.WriteUInt32LittleEndian(dib[20..], imageSize);
        BinaryPrimitives.WriteUInt32LittleEndian(dib[40..], 0x00FF0000); // red
        BinaryPrimitives.WriteUInt32LittleEndian(dib[44..], 0x0000FF00); // green
        BinaryPrimitives.WriteUInt32LittleEndian(dib[48..], 0x000000FF); // blue
        BinaryPrimitives.WriteUInt32LittleEndian(dib[52..], 0xFF000000); // alpha
        BinaryPrimitives.WriteUInt32LittleEndian(dib[56..], 0x73524742); // 'sRGB'
    }

    private static void ToBgra(ReadOnlySpan<byte> rgba, Span<byte> bgra)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }
    }
}
