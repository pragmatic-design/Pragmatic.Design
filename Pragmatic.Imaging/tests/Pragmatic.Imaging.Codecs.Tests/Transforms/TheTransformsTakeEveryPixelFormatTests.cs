using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Transforms;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests.Transforms;

/// <summary>
///     Every operation takes every pixel format and returns the format it was given. The geometry moves whole pixels,
///     so it is checked exactly against pixels moved by hand; the arithmetic is checked against the same operation on
///     8-bit RGBA, after narrowing, within one unit (two for sharpening, which doubles a rounding): a deep format rounds
///     once at the end, 8-bit RGBA at every step.
/// </summary>
public class TheTransformsTakeEveryPixelFormatTests
{
    private const int Width = 13;
    private const int Height = 7;

    public static TheoryData<PixelFormat> Formats => [.. Enum.GetValues<PixelFormat>()];

    public static TheoryData<PixelFormat> DeepOrNarrowFormats => [.. Enum.GetValues<PixelFormat>().Where(f => f != PixelFormat.Rgba8)];

    [Theory]
    [MemberData(nameof(Formats))]
    public void The_geometry_moves_whole_pixels_in_every_format(PixelFormat format)
    {
        using var source = Distinct(format);
        var pixels = PixelsOf(source);

        Check(PixelGeometry.Crop(source, 2, 1, 9, 5), 9, 5, (x, y) => pixels[y + 1][x + 2]);
        Check(PixelGeometry.Rotate90(source), Height, Width, (x, y) => pixels[Height - 1 - x][y]);
        Check(PixelGeometry.Rotate180(source), Width, Height, (x, y) => pixels[Height - 1 - y][Width - 1 - x]);
        Check(PixelGeometry.Rotate270(source), Height, Width, (x, y) => pixels[x][Width - 1 - y]);
        Check(PixelGeometry.Transpose(source), Height, Width, (x, y) => pixels[x][y]);
        Check(PixelGeometry.Transverse(source), Height, Width, (x, y) => pixels[Height - 1 - x][Width - 1 - y]);

        using var horizontal = Distinct(format);
        PixelGeometry.FlipHorizontal(horizontal);
        CheckInPlace(horizontal, (x, y) => pixels[y][Width - 1 - x]);

        using var vertical = Distinct(format);
        PixelGeometry.FlipVertical(vertical);
        CheckInPlace(vertical, (x, y) => pixels[Height - 1 - y][x]);

        void Check(PixelBuffer result, int width, int height, Func<int, int, byte[]> expected)
        {
            using (result)
            {
                Assert.Equal(format, result.Format);
                Assert.Equal((width, height), (result.Width, result.Height));
                CheckInPlace(result, expected);
            }
        }
    }

    [Theory]
    [MemberData(nameof(DeepOrNarrowFormats))]
    public void The_arithmetic_on_every_format_agrees_with_8_bit_rgba(PixelFormat format)
    {
        var rgba = Photo(format);
        AgreesWith(rgba, format, "Lanczos 9x4", image => Resampler.Resize(image, 9, 4));
        AgreesWith(rgba, format, "halving 5x3", image => Resampler.Resize(image, 5, 3, ResizeFilter.Triangle));
        AgreesWith(rgba, format, "Catmull-Rom 31x17", image => Resampler.Resize(image, 31, 17, ResizeFilter.CatmullRom));
        AgreesWith(rgba, format, "blur", image => GaussianBlur.Blur(image, 1.5f));
        // Sharpening adds the difference from a blur back: 8-bit RGBA rounds the blur first (half a unit), the
        // difference doubles it, and the result rounds again, so it can sit 1.5 from the exact value where a deep
        // format sits within 0.5. Two units apart, at most.
        AgreesWith(rgba, format, "sharpen", image => GaussianBlur.Unsharpen(image, 1f, 2), tolerance: 2);
        AgreesWith(rgba, format, "greyscale", image => InPlace(image, PixelAdjustments.Grayscale));
        AgreesWith(rgba, format, "brightness +37", image => InPlace(image, i => PixelAdjustments.Brightness(i, 37)));
        AgreesWith(rgba, format, "brightness -50", image => InPlace(image, i => PixelAdjustments.Brightness(i, -50)));
        AgreesWith(rgba, format, "contrast", image => InPlace(image, i => PixelAdjustments.Contrast(i, 25f)));
    }

    [Theory]
    [MemberData(nameof(DeepOrNarrowFormats))]
    public void Narrowing_to_8_bit_rgba_gives_the_pixels_a_format_was_widened_from(PixelFormat format)
    {
        var rgba = Photo(format);
        using var deep = From(rgba, format);
        using var narrowed = PixelConversion.ToRgba8(deep);

        Assert.Equal(PixelFormat.Rgba8, narrowed.Format);
        Assert.True(narrowed.Pixels.SequenceEqual(rgba), $"{format} narrows to other pixels than it was made from");
    }

    [Fact]
    public void Widening_8_bit_rgba_to_16_bits_is_exact()
    {
        var rgba = SyntheticPhoto.Rgba(Width, Height);
        using var image = new PixelBuffer(Width, Height);
        rgba.CopyTo(image.Pixels);

        using var wide = PixelConversion.ToRgba16(image);

        var samples = MemoryMarshal.Cast<byte, ushort>(wide.Pixels);
        for (var i = 0; i < rgba.Length; i++)
            Assert.Equal(rgba[i] * 257, samples[i]);
    }

    [Fact]
    public void A_buffer_already_in_the_format_asked_for_is_returned_as_it_is()
    {
        using var rgba = new PixelBuffer(2, 2);
        using var wide = new PixelBuffer(2, 2, PixelFormat.Rgba16);

        Assert.Same(rgba, PixelConversion.ToRgba8(rgba));
        Assert.Same(wide, PixelConversion.ToRgba16(wide));
    }

    // Every pixel's bytes distinct from every other's, so a pixel moved to the wrong place shows.
    private static PixelBuffer Distinct(PixelFormat format)
    {
        var image = new PixelBuffer(Width, Height, format);
        var bytes = image.Pixels;
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 7 + i / image.BytesPerPixel);
        return image;
    }

    private static byte[][][] PixelsOf(PixelBuffer image)
    {
        var rows = new byte[image.Height][][];
        for (var y = 0; y < image.Height; y++)
        {
            rows[y] = new byte[image.Width][];
            for (var x = 0; x < image.Width; x++)
                rows[y][x] = image.Row(y).Slice(x * image.BytesPerPixel, image.BytesPerPixel).ToArray();
        }

        return rows;
    }

    private static void CheckInPlace(PixelBuffer image, Func<int, int, byte[]> expected)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
                Assert.True(image.Row(y).Slice(x * image.BytesPerPixel, image.BytesPerPixel).SequenceEqual(expected(x, y)), $"{image.Format}: pixel ({x}, {y})");
        }
    }

    // A photo the format can hold exactly: grey for the grey formats, opaque for those without alpha.
    private static byte[] Photo(PixelFormat format)
    {
        var rgba = SyntheticPhoto.Rgba(Width, Height);
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (format.IsGray())
                rgba[i + 1] = rgba[i + 2] = rgba[i];
            if (!format.HasAlpha())
                rgba[i + 3] = 255;
        }

        return rgba;
    }

    // The 8-bit RGBA pixels in the format: 16-bit samples as v × 257, floats as v / 255.
    private static PixelBuffer From(byte[] rgba, PixelFormat format)
    {
        var image = new PixelBuffer(Width, Height, format);
        var channels = format.Channels();
        var bits = format.BitsPerChannel();
        for (var p = 0; p < Width * Height; p++)
        {
            for (var c = 0; c < channels; c++)
            {
                var value = rgba[p * 4 + c];
                var at = p * channels + c;
                switch (bits)
                {
                    case 8:
                        image.Pixels[at] = value;
                        break;
                    case 16:
                        BinaryPrimitives.WriteUInt16LittleEndian(image.Pixels[(at * 2)..], (ushort)(value * 257));
                        break;
                    default:
                        MemoryMarshal.Cast<byte, float>(image.Pixels)[at] = value / 255f;
                        break;
                }
            }
        }

        return image;
    }

    private static void AgreesWith(byte[] rgba, PixelFormat format, string name, Func<PixelBuffer, PixelBuffer> operation, int tolerance = 1)
    {
        using var narrow = new PixelBuffer(Width, Height);
        rgba.CopyTo(narrow.Pixels);
        using var deep = From(rgba, format);

        var expected = operation(narrow);
        var actual = operation(deep);
        try
        {
            Assert.Equal(format, actual.Format);
            using var narrowed = PixelConversion.ToRgba8(actual);
            Assert.Equal((expected.Width, expected.Height), (narrowed.Width, narrowed.Height));
            var a = narrowed.Pixels;
            var e = expected.Pixels;
            for (var i = 0; i < e.Length; i++)
            {
                // A grey format keeps one channel: compare it, and the alpha only where the format has one.
                var channel = i % 4;
                if ((format.IsGray() && channel is 1 or 2) || (!format.HasAlpha() && channel == 3))
                    continue;
                Assert.True(Math.Abs(a[i] - e[i]) <= tolerance, $"{format}, {name}: byte {i} is {a[i]}, 8-bit RGBA gives {e[i]}");
            }
        }
        finally
        {
            if (!ReferenceEquals(expected, narrow))
                expected.Dispose();
            if (!ReferenceEquals(actual, deep))
                actual.Dispose();
        }
    }

    private static PixelBuffer InPlace(PixelBuffer image, Action<PixelBuffer> operation)
    {
        operation(image);
        return image;
    }
}
