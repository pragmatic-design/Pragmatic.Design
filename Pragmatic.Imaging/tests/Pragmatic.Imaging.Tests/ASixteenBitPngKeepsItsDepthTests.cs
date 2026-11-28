using System.Runtime.InteropServices;
using Pragmatic.Testing.Assertions;
using SkiaSharp;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     A 16-bit PNG goes through the pipeline at its own depth: decoded to 16-bit samples, cropped as whole pixels, and
///     written back as a 16-bit file, so no sample loses its low byte on the way. Skia writes the source and a reader
///     written from the specification reads the result (<see cref="SixteenBitPngReader" />), so neither end is the
///     module's own codec.
/// </summary>
public class ASixteenBitPngKeepsItsDepthTests
{
    private const int Width = 61;
    private const int Height = 37;

    [Fact]
    public void A_sixteen_bit_png_round_trips_through_decode_crop_and_encode_without_loss()
    {
        var source = Samples();
        var png = EncodeWithSkia(source, Width, Height);
        png[24].Should().Be((byte)16); // IHDR bit depth: Skia wrote a 16-bit file, so the test covers that path.
        SixteenBitPngReader.Read(png).Samples.Should().Equal(source); // the test's reader reads Skia's file right

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Crop(5, 3, 40, 29);
        var encoded = pipeline.Encode(ImageFormat.Png);

        encoded[24].Should().Be((byte)16);
        var (decoded, width, height) = SixteenBitPngReader.Read(encoded);
        (width, height).Should().Be((40, 29));
        decoded.Should().Equal(Crop(source, 5, 3, 40, 29));
    }

    [Fact]
    public void An_8_bit_format_gets_the_samples_rounded_to_8_bits()
    {
        var source = Samples();
        using var pipeline = ImagePipeline.Load(EncodeWithSkia(source, Width, Height));

        var bmp = pipeline.Encode(ImageFormat.Bmp);

        // Unpremultiplied, so a translucent pixel reads back without a rounding of Skia's own.
        using var codec = SKCodec.Create(SKData.CreateCopy(bmp)) ?? throw new InvalidOperationException("Skia cannot read the BMP.");
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        codec.GetPixels(info, bitmap.GetPixels()).Should().Be(SKCodecResult.Success);
        var decoded = new byte[info.BytesSize];
        Marshal.Copy(bitmap.GetPixels(), decoded, 0, decoded.Length);

        decoded.Should().Equal(source.Select(v => (byte)((v + 128) / 257)).ToArray());
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.WebP)]
    [InlineData(ImageFormat.Gif)]
    public void A_lossy_or_paletted_format_encodes_the_image_rounded_to_8_bits(ImageFormat format)
    {
        var png = EncodeWithSkia(Samples(), Width, Height);
        using var deep = ImagePipeline.Load(png);
        using var narrowed = ImagePipeline.Load(NarrowedPng(png));

        deep.Encode(format).Should().Equal(narrowed.Encode(format));
    }

    // The same image as an 8-bit PNG, each sample rounded as the native library rounds it.
    private static byte[] NarrowedPng(byte[] png)
    {
        using var decoded = Codecs.Png.PngDecoder.Decode(png);
        using var rgba = Codecs.PixelConversion.ToRgba8(decoded);
        using var output = new MemoryStream();
        Codecs.Png.PngEncoder.Write(rgba, output);
        return output.ToArray();
    }

    // Every sample a different 16-bit value, with a low byte that is not the high one: a pipeline that narrows to
    // 8 bits and widens back (v * 257) cannot reproduce it.
    private static ushort[] Samples()
    {
        var samples = new ushort[Width * Height * 4];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (ushort)(i * 2654435761u >> 16);
        for (var p = 0; p < Width * Height; p++)
            samples[p * 4 + 3] = (ushort)(0x8000 + p * 13);
        return samples;
    }

    private static ushort[] Crop(ushort[] samples, int x, int y, int width, int height)
    {
        var result = new ushort[width * height * 4];
        for (var row = 0; row < height; row++)
            samples.AsSpan(((y + row) * Width + x) * 4, width * 4).CopyTo(result.AsSpan(row * width * 4));
        return result;
    }

    private static byte[] EncodeWithSkia(ushort[] samples, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba16161616, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), 0, bitmap.GetPixels(), samples.Length * 2);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
