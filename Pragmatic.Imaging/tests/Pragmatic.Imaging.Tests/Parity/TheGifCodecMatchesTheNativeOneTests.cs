using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Gif;
using Pragmatic.Imaging.Codecs.Png;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     The managed GIF codec against the native one it replaces. On every file Pillow wrote for it, and on files
///     made by hand for what Pillow does not write, it reads the first frame the native library read, exactly, and
///     refuses what it refused; on images of at most 256 colours it writes the native library's file, byte for byte.
/// </summary>
public sealed class TheGifCodecMatchesTheNativeOneTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "References", "Gif");
    private static readonly string Photographs = Path.Combine(AppContext.BaseDirectory, "References", "Photographs");

    private static readonly byte[] Four = [10, 20, 30, 200, 100, 50, 0, 255, 0, 9, 8, 7];

    [Fact]
    public void TheFiles_AreEachListedWithTheirProvenance() => ReferenceManifest.AssertComplete(Files);

    [NativeRequiredFact]
    public void Decode_OfEveryFile_ReadsWhatTheNativeDecoderRead()
    {
        var failures = new List<string>();
        var files = Directory.EnumerateFiles(Files, "*.gif").Order(StringComparer.Ordinal).ToArray();
        foreach (var path in files)
        {
            var problem = Compare(File.ReadAllBytes(path));
            if (problem is not null)
                failures.Add($"{Path.GetFileName(path)}: {problem}");
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
        files.Length.Should().Be(18);
    }

    public static TheoryData<string, byte[]> MadeByHand()
    {
        var pixels = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0, 1, 1, 2, 2 };
        GifStreams.Frame Frame(int left = 0, int top = 0, int width = 4, int height = 3) => new(left, top, width, height, pixels);
        return new()
        {
            { "a frame the size of the screen", GifStreams.File(4, 3, Four, Frame()) },
            { "a frame off the origin, on a larger screen", GifStreams.File(10, 8, Four, Frame(3, 2)) },
            { "a frame past the screen's right and bottom edges", GifStreams.File(6, 4, Four, Frame(4, 2)) },
            { "a frame wholly off the screen", GifStreams.File(3, 3, Four, Frame(5, 5)) },
            { "a transparent index", GifStreams.File(4, 3, Four, Frame(), GifStreams.Control(transparent: 2)) },
            { "two control extensions, the second without transparency", GifStreams.File(4, 3, Four, Frame(), GifStreams.Control(1), GifStreams.Control(null)) },
            { "indices past a two-entry table", GifStreams.File(4, 3, [1, 2, 3, 4, 5, 6], Frame()) },
            { "a local table over a global one", GifStreams.File(4, 3, Four, Frame() with { LocalTable = [9, 9, 9, 99, 99, 99, 199, 199, 199, 255, 0, 255] }) },
            { "an interlaced frame of three rows", GifStreams.File(4, 3, Four, Frame() with { Interlaced = true }) },
            { "no clear code before the first", GifStreams.File(4, 3, Four, Frame() with { OmitClear = true }) },
            { "codes past the frame's pixels", GifStreams.File(4, 3, Four, Frame() with { ExtraCodes = [1, 2, 3, 0, 1] }) },
            { "a comment and an application extension", GifStreams.File(4, 3, Four, Frame(), GifStreams.Extension(0xFE, 65, 66), GifStreams.Extension(0xFF, 1, 2, 3)) },
            { "an unknown extension", GifStreams.File(4, 3, Four, Frame(), GifStreams.Extension(0x99, 1)) },
            { "a control extension of three bytes", GifStreams.File(4, 3, Four, Frame(), GifStreams.Control(1, length: 3)) },
            { "no colour table at all", GifStreams.File(4, 3, null, Frame()) },
            { "a minimum code size of 12", GifStreams.File(4, 3, Four, Frame() with { MinimumCodeSize = 12 }) },
            { "an end code before the last pixel", GifStreams.File(4, 3, Four, new GifStreams.Frame(0, 0, 4, 3, [0, 1, 2])) },
            { "a code past the next to be defined", GifStreams.File(4, 3, Four, new GifStreams.Frame(0, 0, 4, 3, [0, 1, 7, 2])) },
            { "image data cut short", GifStreams.File(4, 3, Four, Frame() with { CutDataAt = 3 }) },
            { "a frame of no pixels", GifStreams.File(4, 3, Four, new GifStreams.Frame(0, 0, 0, 3, [])) },
            { "an interlaced frame of no rows", GifStreams.File(4, 3, Four, new GifStreams.Frame(0, 0, 4, 0, []) { Interlaced = true }) },
        };
    }

    [NativeRequiredTheory]
    [MemberData(nameof(MadeByHand))]
    public void Decode_OfAFileMadeByHand_ReadsWhatTheNativeDecoderRead(string what, byte[] file)
    {
        var problem = Compare(file);
        (problem is null).Should().BeTrue($"{what}: {problem}");
    }

    [NativeRequiredFact]
    public void Encode_OfImagesOfAtMost256Colours_IsTheNativeEncodersFile()
    {
        var images = new List<(string, PixelBuffer)>
        {
            ("every alpha over changing colours", Make(17, 15, (x, y, i) => ((byte)(i * 7), (byte)(255 - i), (byte)(i * 13), (byte)i))),
            ("40 colours", Make(64, 48, (x, y, i) => Colour((x / 5 + y / 3) % 40))),
            ("a grey ramp with a transparent band", Make(256, 3, (x, y, i) => ((byte)x, (byte)x, (byte)x, (byte)(x is > 100 and < 120 ? 0 : 255)))),
            ("one colour", Make(5, 5, (x, y, i) => ((byte)9, (byte)8, (byte)7, (byte)255))),
            ("one pixel", Make(1, 1, (x, y, i) => ((byte)200, (byte)100, (byte)50, (byte)255))),
            ("a photograph's 256 colours", Pillow("coffee.gif")),
            ("a long image of 256 colours", Make(1024, 300, (x, y, i) => Colour((x * 7 + y * 13 + x * y) % 256))),
        };

        foreach (var (name, image) in images)
        {
            using (image)
            {
                var native = NativeReference.EncodeGif(new NativeReference.Image(image.Pixels.ToArray(), image.Width, image.Height));
                var managed = GifEncoder.Encode(image);
                managed.AsSpan().SequenceEqual(native).Should().BeTrue($"{name}: the managed file is {managed.Length} B, the native {native.Length} B");
            }
        }
    }

    // Null when the managed decoder reads what the native one read, or refuses as it did.
    private static string? Compare(byte[] encoded)
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
        try
        {
            managed = GifDecoder.Decode(encoded);
        }
        catch (ImageDecodeException)
        {
            managed = null;
        }

        using (managed)
        {
            if (native is null || managed is null)
                return native is null && managed is null ? null : $"native {(native is null ? "refused" : "read")} it, managed {(managed is null ? "refused" : "read")} it";
            if (managed.Width != native.Value.Width || managed.Height != native.Value.Height)
                return $"{managed.Width} x {managed.Height}, native {native.Value.Width} x {native.Value.Height}";
            return managed.Pixels.SequenceEqual(native.Value.Rgba) ? null : "the pixels differ";
        }
    }

    // A Pillow file's pixels, as the native library read them: 256 colours of a photograph.
    private static PixelBuffer Pillow(string file)
    {
        var native = NativeReference.Decode(File.ReadAllBytes(Path.Combine(Files, file)));
        var image = new PixelBuffer(native.Width, native.Height);
        native.Rgba.CopyTo(image.Pixels);
        return image;
    }

    private static PixelBuffer Make(int width, int height, Func<int, int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var image = new PixelBuffer(width, height);
        var pixels = image.Pixels;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var (r, g, b, a) = pixel(x, y, i);
                pixels[i * 4] = r;
                pixels[i * 4 + 1] = g;
                pixels[i * 4 + 2] = b;
                pixels[i * 4 + 3] = a;
            }
        }

        return image;
    }

    private static (byte, byte, byte, byte) Colour(int k) => ((byte)(k * 37 % 256), (byte)(k * 91 % 256), (byte)(k * 53 % 256), 255);
}
