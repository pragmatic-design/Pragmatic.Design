using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     The sample buffer of a 16-bit or floating-point TIFF into a buffer that keeps the depth: grey, RGB and RGBA at
///     16 bits as they are, floats as they are (RGB gaining an alpha of 1), 16-bit CMYK as the 16-bit RGB the image crate
///     computes before it narrows. Narrowing the result with <see cref="PixelConversion.ToRgba8" /> gives what
///     <see cref="TiffPixels" /> gives, the native library's pixels. Every shallower TIFF decodes to 8-bit RGBA.
/// </summary>
internal static class TiffDeepPixels
{
    public static PixelFormat FormatOf(TiffPixelFormat format) => format switch
    {
        TiffPixelFormat.Gray16 => PixelFormat.Gray16,
        TiffPixelFormat.Rgb16 or TiffPixelFormat.Cmyk16 => PixelFormat.Rgb16,
        TiffPixelFormat.Rgba16 => PixelFormat.Rgba16,
        TiffPixelFormat.Rgb32Float or TiffPixelFormat.Rgba32Float => PixelFormat.RgbaFloat,
        _ => PixelFormat.Rgba8,
    };

    public static void Convert(TiffImage image, in TiffLayout layout, ReadOnlySpan<byte> samples, PixelBuffer output)
    {
        var size = image.Bits / 8;
        var bytes = layout.DataSamples * size;
        Span<byte> pixel = stackalloc byte[bytes];
        for (var y = 0; y < image.Height; y++)
        {
            var target = output.Row(y);
            if (layout.Planes == 1)
            {
                var row = samples.Slice((int)(y * layout.RowStride), image.Width * bytes);
                if (image.Format is TiffPixelFormat.Gray16 or TiffPixelFormat.Rgb16 or TiffPixelFormat.Rgba16 or TiffPixelFormat.Rgba32Float)
                {
                    row.CopyTo(target);
                    continue;
                }

                for (var x = 0; x < image.Width; x++)
                    Pixel(image.Format, row.Slice(x * bytes, bytes), target.Slice(x * output.BytesPerPixel, output.BytesPerPixel));
                continue;
            }

            if (image.Format == TiffPixelFormat.Cmyk16)
                throw new ImageDecodeException("TIFF: planar CMYK is not read.");
            for (var x = 0; x < image.Width; x++)
            {
                for (var p = 0; p < layout.Planes; p++)
                    samples.Slice((int)(p * layout.PlaneStride + y * layout.RowStride + x * size), size).CopyTo(pixel[(p * size)..]);
                Pixel(image.Format, pixel, target.Slice(x * output.BytesPerPixel, output.BytesPerPixel));
            }
        }
    }

    // One pixel of the file's samples into one of the output's.
    private static void Pixel(TiffPixelFormat format, ReadOnlySpan<byte> source, Span<byte> target)
    {
        switch (format)
        {
            case TiffPixelFormat.Rgb32Float:
                source[..12].CopyTo(target);
                MemoryMarshal.Cast<byte, float>(target)[3] = 1f;
                return;
            case TiffPixelFormat.Cmyk16:
                // The image crate's conversion, in single precision and truncated, kept at 16 bits.
                var k = 1f - BinaryPrimitives.ReadUInt16LittleEndian(source[6..]) / 65535f;
                for (var c = 0; c < 3; c++)
                    BinaryPrimitives.WriteUInt16LittleEndian(target[(c * 2)..], (ushort)((65535f - BinaryPrimitives.ReadUInt16LittleEndian(source[(c * 2)..])) * k));
                return;
            default:
                source.CopyTo(target);
                return;
        }
    }
}
