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
            if (managed.Pixels.SequenceEqual(native.Value.Rgba))
                return null;
            var pixels = managed.Pixels;
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
