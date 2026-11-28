using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Pragmatic.Imaging.Codecs.Png;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     A 16-bit PNG decodes at its depth, in the layout closest to its colour type, and the encoder writes a deep buffer
///     at 16 bits. The files are built here from the specification (chunks, unfiltered rows, the Adam7 passes), so the
///     decoder is read against bytes it did not write.
/// </summary>
public class PngSixteenBitTests
{
    private const int Width = 11;
    private const int Height = 9;

    [Fact]
    public void A_grey_file_decodes_to_gray16()
    {
        using var image = PngDecoder.Decode(Png(colorType: 0, channels: 1));

        Assert.Equal(PixelFormat.Gray16, image.Format);
        AssertSamples(image, channels: 1, (p, c) => Sample(p, c));
    }

    [Fact]
    public void An_rgb_file_decodes_to_rgb16()
    {
        using var image = PngDecoder.Decode(Png(colorType: 2, channels: 3));

        Assert.Equal(PixelFormat.Rgb16, image.Format);
        AssertSamples(image, channels: 3, (p, c) => Sample(p, c));
    }

    [Fact]
    public void An_rgba_file_interlaced_decodes_to_rgba16()
    {
        using var image = PngDecoder.Decode(Png(colorType: 6, channels: 4, interlaced: true));

        Assert.Equal(PixelFormat.Rgba16, image.Format);
        AssertSamples(image, channels: 4, (p, c) => Sample(p, c));
    }

    [Fact]
    public void A_grey_and_alpha_file_decodes_to_rgba16()
    {
        using var image = PngDecoder.Decode(Png(colorType: 4, channels: 2));

        Assert.Equal(PixelFormat.Rgba16, image.Format);
        AssertSamples(image, channels: 4, (p, c) => c < 3 ? Sample(p, 0) : Sample(p, 1));
    }

    [Fact]
    public void An_rgb_file_with_a_colour_key_decodes_to_rgba16_with_the_key_transparent()
    {
        const int keyed = 17;
        var key = new byte[6];
        for (var c = 0; c < 3; c++)
            BinaryPrimitives.WriteUInt16BigEndian(key.AsSpan(c * 2), Sample(keyed, c));

        using var image = PngDecoder.Decode(Png(colorType: 2, channels: 3, transparency: key));

        Assert.Equal(PixelFormat.Rgba16, image.Format);
        AssertSamples(image, channels: 4, (p, c) => c < 3 ? Sample(p, c) : p == keyed ? (ushort)0 : ushort.MaxValue);
    }

    [Fact]
    public void A_grey_file_with_a_colour_key_decodes_to_rgba16_with_the_key_transparent()
    {
        const int keyed = 5;
        var key = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(key, Sample(keyed, 0));

        using var image = PngDecoder.Decode(Png(colorType: 0, channels: 1, transparency: key));

        Assert.Equal(PixelFormat.Rgba16, image.Format);
        AssertSamples(image, channels: 4, (p, c) => c < 3 ? Sample(p, 0) : p == keyed ? (ushort)0 : ushort.MaxValue);
    }

    [Fact]
    public void An_8_bit_file_still_decodes_to_rgba8()
    {
        using var source = new PixelBuffer(Width, Height);
        source.Pixels.Fill(200);
        using var png = new MemoryStream();
        PngEncoder.Write(source, png);

        using var image = PngDecoder.Decode(png.ToArray());

        Assert.Equal(PixelFormat.Rgba8, image.Format);
    }

    [Theory]
    [InlineData(PixelFormat.Gray16, 0)]
    [InlineData(PixelFormat.Rgb16, 2)]
    [InlineData(PixelFormat.Rgba16, 6)]
    public void A_deep_buffer_is_written_at_16_bits_and_reads_back_the_same(PixelFormat format, byte colorType)
    {
        using var source = new PixelBuffer(Width, Height, format);
        var samples = MemoryMarshal.Cast<byte, ushort>(source.Pixels);
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (ushort)(i * 40503 + 7);

        var png = Encode(source);
        using var decoded = PngDecoder.Decode(png);

        Assert.Equal(16, png[24]);
        Assert.Equal(colorType, png[25]);
        Assert.Equal(format, decoded.Format);
        Assert.True(decoded.Pixels.SequenceEqual(source.Pixels), $"{format} does not read back the samples it was written with");
    }

    [Fact]
    public void An_opaque_rgba16_buffer_is_written_as_rgb()
    {
        using var source = new PixelBuffer(Width, Height, PixelFormat.Rgba16);
        var samples = MemoryMarshal.Cast<byte, ushort>(source.Pixels);
        for (var i = 0; i < samples.Length; i++)
            samples[i] = i % 4 == 3 ? ushort.MaxValue : (ushort)(i * 40503);

        var png = Encode(source);
        using var decoded = PngDecoder.Decode(png);

        Assert.Equal(2, png[25]);
        Assert.Equal(PixelFormat.Rgb16, decoded.Format);
        var rgb = MemoryMarshal.Cast<byte, ushort>(decoded.Pixels);
        for (var p = 0; p < Width * Height; p++)
        {
            for (var c = 0; c < 3; c++)
                Assert.Equal(samples[p * 4 + c], rgb[p * 3 + c]);
        }
    }

    [Fact]
    public void A_float_buffer_is_written_at_16_bits()
    {
        using var source = new PixelBuffer(Width, Height, PixelFormat.RgbaFloat);
        var floats = MemoryMarshal.Cast<byte, float>(source.Pixels);
        for (var i = 0; i < floats.Length; i++)
            floats[i] = i % 4 == 3 ? 0.5f : i / (float)floats.Length;

        var png = Encode(source);
        using var decoded = PngDecoder.Decode(png);

        Assert.Equal(16, png[24]);
        Assert.Equal(PixelFormat.Rgba16, decoded.Format);
        var samples = MemoryMarshal.Cast<byte, ushort>(decoded.Pixels);
        for (var i = 0; i < floats.Length; i++)
            Assert.Equal((ushort)MathF.Round(floats[i] * 65535f, MidpointRounding.AwayFromZero), samples[i]);
    }

    private static ushort Sample(int pixel, int channel) => (ushort)(pixel * 7919 + channel * 104729 + 0x00A5);

    private static byte[] Encode(PixelBuffer image)
    {
        using var output = new MemoryStream();
        PngEncoder.Write(image, output);
        return output.ToArray();
    }

    private static void AssertSamples(PixelBuffer image, int channels, Func<int, int, ushort> expected)
    {
        var samples = MemoryMarshal.Cast<byte, ushort>(image.Pixels);
        Assert.Equal(Width * Height * channels, samples.Length);
        for (var p = 0; p < Width * Height; p++)
        {
            for (var c = 0; c < channels; c++)
                Assert.True(expected(p, c) == samples[p * channels + c], $"pixel {p}, channel {c}: {samples[p * channels + c]}, expected {expected(p, c)}");
        }
    }

    // A 16-bit file of Sample(pixel, channel), every row unfiltered; Adam7 passes when interlaced.
    private static byte[] Png(byte colorType, int channels, bool interlaced = false, byte[]? transparency = null)
    {
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), Height);
        ihdr[8] = 16;
        ihdr[9] = colorType;
        ihdr[12] = interlaced ? (byte)1 : (byte)0;
        Chunk(file, "IHDR", ihdr);
        if (transparency is not null)
            Chunk(file, "tRNS", transparency);

        using var raw = new MemoryStream();
        var sample = new byte[2];
        (int X, int Y, int Dx, int Dy)[] passes = interlaced
            ? [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]
            : [(0, 0, 1, 1)];
        foreach (var (px, py, dx, dy) in passes)
        {
            for (var y = py; y < Height; y += dy)
            {
                if (px >= Width)
                    break;
                raw.WriteByte(0);
                for (var x = px; x < Width; x += dx)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(sample, Sample(y * Width + x, c));
                        raw.Write(sample);
                    }
                }
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw.ToArray());
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(typed, 0);
        data.CopyTo(typed, 4);
        file.Write(typed);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(typed));
        file.Write(crc);
    }
}
