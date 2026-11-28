using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs;

/// <summary>
///     Converts a buffer to 8-bit RGBA, for the encoders whose format has nothing deeper (JPEG, WebP, BMP, GIF): the
///     pipeline keeps an image's own depth to the end, and the narrowing happens here, once, at the encode. A 16-bit
///     sample becomes <c>round(v × 255 / 65535)</c>, the native library's conversion; a float one is clamped to 0..1 (not a
///     number counting as 1) and rounded half away from zero, as the image crate does; grey fills the three colours, and a format without alpha is opaque.
/// </summary>
public static class PixelConversion
{
    /// <summary>
    ///     <paramref name="image" /> itself when it is already 8-bit RGBA, otherwise a new buffer the caller disposes. An
    ///     encoder writes <c>using var owned = ...</c> only for the second case: compare the two references.
    /// </summary>
    public static PixelBuffer ToRgba8(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Format == PixelFormat.Rgba8)
            return image;

        var result = new PixelBuffer(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
            NarrowRow(image.Format, image.Row(y), result.Row(y), image.Width);
        return result;
    }

    /// <summary>
    ///     <paramref name="image" /> itself when it is already 16-bit RGBA, otherwise a new buffer the caller disposes: for
    ///     the encoders that write 16 bits. An 8-bit sample becomes <c>v × 257</c>, exactly; a float one is clamped and
    ///     rounded to 0..65535.
    /// </summary>
    public static PixelBuffer ToRgba16(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Format == PixelFormat.Rgba16)
            return image;

        var result = new PixelBuffer(image.Width, image.Height, PixelFormat.Rgba16);
        var channels = image.Format.Channels();
        var deep = image.Format.BitsPerChannel();
        for (var y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);
            var target = MemoryMarshal.Cast<byte, ushort>(result.Row(y));
            for (var x = 0; x < image.Width; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    target[x * 4 + c] = c == 3 && channels < 4
                        ? ushort.MaxValue
                        : Widened(row, deep, x * channels + Math.Min(c, channels - 1));
                }
            }
        }

        return result;
    }

    private static ushort Widened(ReadOnlySpan<byte> row, int bits, int sample) => bits switch
    {
        8 => (ushort)(row[sample] * 257),
        16 => BinaryPrimitives.ReadUInt16LittleEndian(row[(sample * 2)..]),
        _ => (ushort)MathF.Round(UnitClamp(MemoryMarshal.Cast<byte, float>(row)[sample]) * 65535f, MidpointRounding.AwayFromZero),
    };

    private static void NarrowRow(PixelFormat format, ReadOnlySpan<byte> row, Span<byte> rgba, int width)
    {
        for (var x = 0; x < width; x++)
        {
            var o = rgba.Slice(x * 4, 4);
            switch (format)
            {
                case PixelFormat.Rgba16:
                    for (var c = 0; c < 4; c++)
                        o[c] = From16(row, x * 4 + c);
                    break;
                case PixelFormat.RgbaFloat:
                    var floats = MemoryMarshal.Cast<byte, float>(row);
                    for (var c = 0; c < 4; c++)
                        o[c] = (byte)MathF.Round(UnitClamp(floats[x * 4 + c]) * 255f, MidpointRounding.AwayFromZero);
                    break;
                case PixelFormat.Rgb8:
                    row.Slice(x * 3, 3).CopyTo(o);
                    o[3] = 255;
                    break;
                case PixelFormat.Rgb16:
                    for (var c = 0; c < 3; c++)
                        o[c] = From16(row, x * 3 + c);
                    o[3] = 255;
                    break;
                case PixelFormat.Gray8:
                    o[0] = o[1] = o[2] = row[x];
                    o[3] = 255;
                    break;
                case PixelFormat.Gray16:
                    o[0] = o[1] = o[2] = From16(row, x);
                    o[3] = 255;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Not a pixel format.");
            }
        }
    }

    /// <summary>A float sample clamped to 0..1 as the image crate clamps it: not a number counts as 1.</summary>
    internal static float UnitClamp(float value) => !(value < 1f) ? 1f : MathF.Max(value, 0f);

    private static byte From16(ReadOnlySpan<byte> row, int sample) => (byte)((BinaryPrimitives.ReadUInt16LittleEndian(row[(sample * 2)..]) + 128) / 257);
}
