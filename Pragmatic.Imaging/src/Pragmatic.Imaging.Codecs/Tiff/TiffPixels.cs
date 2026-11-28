using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     The sample buffer to RGBA as the image crate turns it into its pixels and then into RGBA8: one-bit grey
///     stretched to 0 and 255, CMYK to RGB in single precision and truncated, 16 bits to 8 rounded, floats clamped to
///     0 to 1 (not a number counting as 1) and rounded half away from zero.
/// </summary>
internal static class TiffPixels
{
    public static void ToRgba(TiffImage image, in TiffLayout layout, ReadOnlySpan<byte> samples, PixelBuffer output)
    {
        var width = image.Width;
        for (var y = 0; y < image.Height; y++)
        {
            var target = output.Row(y);
            if (layout.Planes > 1)
            {
                Planar(image, layout, samples, y, target);
                continue;
            }

            var row = samples.Slice((int)(y * layout.RowStride), (int)layout.RowStride);
            switch (image.Format)
            {
                case TiffPixelFormat.Gray1:
                    for (var x = 0; x < width; x++)
                        Gray(target, x, (row[x >> 3] >> (7 - (x & 7)) & 1) != 0 ? (byte)255 : (byte)0);
                    break;
                case TiffPixelFormat.Gray8:
                    for (var x = 0; x < width; x++)
                        Gray(target, x, row[x]);
                    break;
                case TiffPixelFormat.Rgba8:
                    row[..(width * 4)].CopyTo(target);
                    break;
                case TiffPixelFormat.Rgb8:
                    Rgb8(row, target, width);
                    break;
                case TiffPixelFormat.Gray16:
                    for (var x = 0; x < width; x++)
                        Gray(target, x, Narrow(row, x));
                    break;
                case TiffPixelFormat.Cmyk8:
                    for (var x = 0; x < width; x++)
                        Cmyk8(row.Slice(x * 4, 4), target.Slice(x * 4, 4));
                    break;
                case TiffPixelFormat.Cmyk16:
                    for (var x = 0; x < width; x++)
                        Cmyk16(row.Slice(x * 8, 8), target.Slice(x * 4, 4));
                    break;
                default:
                    for (var x = 0; x < width; x++)
                        Color(image.Format, row, x * layout.DataSamples, target.Slice(x * 4, 4));
                    break;
            }
        }
    }

    // One sample from each plane, the planes one after the other; CMYK the image crate does not read planar.
    private static void Planar(TiffImage image, in TiffLayout layout, ReadOnlySpan<byte> samples, int y, Span<byte> target)
    {
        if (image.Format is TiffPixelFormat.Cmyk8 or TiffPixelFormat.Cmyk16)
            throw new ImageDecodeException("TIFF: planar CMYK is not read.");
        var size = image.Bits / 8;
        Span<byte> pixel = stackalloc byte[layout.Planes * size];
        for (var x = 0; x < image.Width; x++)
        {
            for (var p = 0; p < layout.Planes; p++)
                samples.Slice((int)(p * layout.PlaneStride + y * layout.RowStride + x * size), size).CopyTo(pixel[(p * size)..]);
            Color(image.Format, pixel, 0, target.Slice(x * 4, 4));
        }
    }

    // A pixel of RGB or RGBA from sample <paramref name="at" /> on, the alpha opaque when there is none.
    private static void Color(TiffPixelFormat format, ReadOnlySpan<byte> row, int at, Span<byte> rgba)
    {
        switch (format)
        {
            case TiffPixelFormat.Rgb8 or TiffPixelFormat.Rgba8:
                rgba[0] = row[at];
                rgba[1] = row[at + 1];
                rgba[2] = row[at + 2];
                rgba[3] = format == TiffPixelFormat.Rgba8 ? row[at + 3] : (byte)255;
                break;
            case TiffPixelFormat.Rgb16 or TiffPixelFormat.Rgba16:
                for (var c = 0; c < 3; c++)
                    rgba[c] = Narrow(row, at + c);
                rgba[3] = format == TiffPixelFormat.Rgba16 ? Narrow(row, at + 3) : (byte)255;
                break;
            case TiffPixelFormat.Rgb32Float or TiffPixelFormat.Rgba32Float:
                for (var c = 0; c < 3; c++)
                    rgba[c] = FromFloat(row, at + c);
                rgba[3] = format == TiffPixelFormat.Rgba32Float ? FromFloat(row, at + 3) : (byte)255;
                break;
            case TiffPixelFormat.Gray8:
                rgba[0] = rgba[1] = rgba[2] = row[at];
                rgba[3] = 255;
                break;
            case TiffPixelFormat.Gray16:
                rgba[0] = rgba[1] = rgba[2] = Narrow(row, at);
                rgba[3] = 255;
                break;
            default:
                throw new ImageDecodeException("TIFF: this pixel type is not read planar.");
        }
    }

    // Three bytes to four: the row's bounds checked once, the pixels written without checks.
    private static void Rgb8(ReadOnlySpan<byte> row, Span<byte> target, int width)
    {
        row = row[..(width * 3)];
        target = target[..(width * 4)];
        ref var source = ref MemoryMarshal.GetReference(row);
        ref var pixel = ref MemoryMarshal.GetReference(target);
        for (var x = 0; x < width; x++)
        {
            ref var s = ref Unsafe.Add(ref source, x * 3);
            ref var t = ref Unsafe.Add(ref pixel, x * 4);
            t = s;
            Unsafe.Add(ref t, 1) = Unsafe.Add(ref s, 1);
            Unsafe.Add(ref t, 2) = Unsafe.Add(ref s, 2);
            Unsafe.Add(ref t, 3) = 255;
        }
    }

    private static void Gray(Span<byte> target, int x, byte value)
    {
        target[x * 4] = target[x * 4 + 1] = target[x * 4 + 2] = value;
        target[x * 4 + 3] = 255;
    }

    // round(v * 255 / 65535), as (v + 128) / 257.
    private static byte Narrow(ReadOnlySpan<byte> row, int sample) => (byte)((BinaryPrimitives.ReadUInt16LittleEndian(row[(sample * 2)..]) + 128) / 257);

    private static byte FromFloat(ReadOnlySpan<byte> row, int sample)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(row[(sample * 4)..]);
        var clamped = !(value < 1f) ? 1f : MathF.Max(value, 0f);
        return (byte)MathF.Round(clamped * 255f, MidpointRounding.AwayFromZero);
    }

    private static void Cmyk8(ReadOnlySpan<byte> cmyk, Span<byte> rgba)
    {
        var k = 1f - cmyk[3] / 255f;
        for (var c = 0; c < 3; c++)
            rgba[c] = (byte)((255f - cmyk[c]) * k);
        rgba[3] = 255;
    }

    private static void Cmyk16(ReadOnlySpan<byte> cmyk, Span<byte> rgba)
    {
        var k = 1f - BinaryPrimitives.ReadUInt16LittleEndian(cmyk[6..]) / 65535f;
        for (var c = 0; c < 3; c++)
        {
            var value = (ushort)((65535f - BinaryPrimitives.ReadUInt16LittleEndian(cmyk[(c * 2)..])) * k);
            rgba[c] = (byte)((value + 128) / 257);
        }

        rgba[3] = 255;
    }
}
