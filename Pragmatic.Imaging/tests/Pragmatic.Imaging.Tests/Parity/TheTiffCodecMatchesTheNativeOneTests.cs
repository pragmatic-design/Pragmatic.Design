using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Tiff;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     The managed TIFF decoder against the native one it replaces: on libtiff's test images and on the files made
///     for every compression, layout and sample type, it reads the first image the native library read, exactly, and
///     refuses what it refused.
/// </summary>
public sealed class TheTiffCodecMatchesTheNativeOneTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "References", "Tiff");

    [Fact]
    public void TheFiles_AreEachListedWithTheirProvenance() => ReferenceManifest.AssertComplete(Files);

    [NativeRequiredFact]
    public void Decode_OfEveryFile_ReadsWhatTheNativeDecoderRead()
    {
        var failures = new List<string>();
        var files = Directory.EnumerateFiles(Files).Where(p => p.EndsWith(".tif") || p.EndsWith(".tiff")).Order(StringComparer.Ordinal).ToArray();
        foreach (var path in files)
        {
            var problem = Compare(File.ReadAllBytes(path), Jpeg.GetValueOrDefault(Path.GetFileName(path)));
            if (problem is not null)
                failures.Add($"{Path.GetFileName(path)}: {problem}");
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
        files.Length.Should().Be(78);
    }

    // The depth each file declares, in its name: what the pipeline then keeps to the encode.
    [Theory]
    [InlineData("chelsea-grey16.tiff", PixelFormat.Gray16)]
    [InlineData("coffee-grey16-white-is-zero.tiff", PixelFormat.Gray16)]
    [InlineData("minisblack-1c-16b.tiff", PixelFormat.Gray16)]
    [InlineData("coffee-rgb16-big-endian.tiff", PixelFormat.Rgb16)]
    [InlineData("coffee-rgb16-predictor.tiff", PixelFormat.Rgb16)]
    [InlineData("rgb-3c-16b.tiff", PixelFormat.Rgb16)]
    [InlineData("coffee-cmyk16.tiff", PixelFormat.Rgb16)]
    [InlineData("coffee-rgba16.tiff", PixelFormat.Rgba16)]
    [InlineData("coffee-float.tiff", PixelFormat.RgbaFloat)]
    [InlineData("coffee-float-predictor.tiff", PixelFormat.RgbaFloat)]
    [InlineData("coffee-float-rgba.tiff", PixelFormat.RgbaFloat)]
    public void Decode_OfADeepFile_KeepsItsDepth(string file, PixelFormat expected)
    {
        using var image = TiffDecoder.Decode(File.ReadAllBytes(Path.Combine(Files, file)));

        image.Format.Should().Be(expected);
    }

    // Written by libtiff, read by the module, written by the module at 16 bits, read back: the same samples.
    [Theory]
    [InlineData("coffee-rgba16.tiff")]
    [InlineData("coffee-rgb16-big-endian.tiff")]
    [InlineData("chelsea-grey16.tiff")]
    public void A_deep_file_through_the_pipeline_is_written_at_its_depth(string file)
    {
        var encoded = File.ReadAllBytes(Path.Combine(Files, file));
        using var original = TiffDecoder.Decode(encoded);
        using var expected = PixelConversion.ToRgba16(original);

        using var pipeline = ImagePipeline.Load(encoded);
        using var written = TiffDecoder.Decode(pipeline.Encode(ImageFormat.Tiff));

        written.Format.Should().Be(PixelFormat.Rgba16);
        written.Pixels.SequenceEqual(expected.Pixels).Should().BeTrue($"{file} lost samples through the pipeline");
    }

    [NativeRequiredFact]
    public void Encode_IsTheNativeEncodersFile()
    {
        var images = new (string Name, int Width, int Height)[]
        {
            ("one pixel", 1, 1), ("one strip", 37, 11), ("strips of 250 rows, the last short", 1000, 777), ("one column", 1, 300),
            ("rows over a megabyte, one a strip", 250_001, 2),
        };
        foreach (var (name, width, height) in images)
        {
            using var image = new PixelBuffer(width, height);
            var pixels = image.Pixels;
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)(i * 31 + i / 7);
            var native = NativeReference.EncodeTiff(new NativeReference.Image(pixels.ToArray(), width, height));
            var managed = TiffEncoder.Encode(image);
            managed.AsSpan().SequenceEqual(native).Should().BeTrue($"{name}: the managed file is {managed.Length} B, the native {native.Length} B");
        }
    }

    // The JPEG strips go through the core's JPEG decoder, which rounds its inverse DCT and its chroma unlike zune-jpeg:
    // lossy data, held within the difference measured on these files.
    private static readonly Dictionary<string, int> Jpeg = new()
    {
        ["coffee-grey-jpeg.tiff"] = 1, ["coffee-rgb-jpeg.tiff"] = 1, ["coffee-tiles-jpeg.tiff"] = 2,
    };

    // Null when the managed decoder reads what the native one read, or refuses as it did.
    private static string? Compare(byte[] encoded, int tolerance = 0)
    {
        NativeReference.Image? native;
        try
        {
            native = NativeReference.Decode(encoded);
        }
        catch (ImagingException)
        {
            native = null;
        }

        PixelBuffer? managed;
        string? refusal = null;
        try
        {
            managed = TiffDecoder.Decode(encoded);
        }
        catch (ImageDecodeException e)
        {
            managed = null;
            refusal = e.Message;
        }

        using (managed)
        {
            if (native is null || managed is null)
                return native is null && managed is null
                    ? null
                    : $"native {(native is null ? "refused" : "read")} it, managed {(managed is null ? $"refused it ({refusal})" : "read it")}";
            if (managed.Width != native.Value.Width || managed.Height != native.Value.Height)
                return $"{managed.Width} x {managed.Height}, native {native.Value.Width} x {native.Value.Height}";

            // A 16-bit or float file keeps its depth; the native library gave its 8-bit RGBA, which is what an encode
            // to an 8-bit format narrows the managed pixels to.
            if (managed.Format != PixelFormat.Rgba8 && managed.Format.BitsPerChannel() == 8)
                return $"an 8-bit file decoded to {managed.Format}";
            using var narrowed = managed.Format == PixelFormat.Rgba8 ? null : PixelConversion.ToRgba8(managed);
            var pixels = (narrowed ?? managed).Pixels;
            if (pixels.SequenceEqual(native.Value.Rgba))
                return null;
            var first = 0;
            while (pixels[first] == native.Value.Rgba[first])
                first++;
            var worst = 0;
            for (var i = 0; i < pixels.Length; i++)
                worst = Math.Max(worst, Math.Abs(pixels[i] - native.Value.Rgba[i]));
            if (worst <= tolerance)
                return null;
            return $"the pixels differ, first at byte {first}: {pixels[first]} against {native.Value.Rgba[first]}, at most by {worst}";
        }
    }
}
