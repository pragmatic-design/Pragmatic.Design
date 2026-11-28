using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Bmp;
using Pragmatic.Imaging.Codecs.Png;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     The managed BMP codec against the native one it replaces. On every file of the BMP Suite's good and
///     questionable sets, it reads the pixels the native library read, exactly, and refuses what it refused;
///     on the photographs, and on an image with every alpha, it writes the file the native library wrote, byte
///     for byte.
/// </summary>
public sealed class TheBmpCodecMatchesTheNativeOneTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "References", "Bmp");
    private static readonly string Photographs = Path.Combine(AppContext.BaseDirectory, "References", "Photographs");

    [Fact]
    public void TheFiles_AreEachListedWithTheirProvenance() => ReferenceManifest.AssertComplete(Files);

    [NativeRequiredFact]
    public void Decode_OfEveryFile_ReadsWhatTheNativeDecoderRead()
    {
        var failures = new List<string>();
        var read = 0;
        foreach (var path in Directory.EnumerateFiles(Files, "*.bmp").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var encoded = File.ReadAllBytes(path);
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
                managed = BmpDecoder.Decode(encoded);
            }
            catch (ImageDecodeException)
            {
                managed = null;
            }

            using (managed)
            {
                if (native is null || managed is null)
                {
                    if (native is not null || managed is not null)
                        failures.Add($"{name}: native {(native is null ? "refused" : "read")} it, managed {(managed is null ? "refused" : "read")} it");
                    continue;
                }

                read++;
                if (managed.Width != native.Value.Width || managed.Height != native.Value.Height)
                    failures.Add($"{name}: {managed.Width} x {managed.Height}, native {native.Value.Width} x {native.Value.Height}");
                else if (!managed.Pixels.SequenceEqual(native.Value.Rgba))
                    failures.Add($"{name}: the pixels differ");
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
        read.Should().Be(58, "the native library read 58 of the 68 files and refused 10 (two more of the suite carry an embedded ICC profile and are not here)");
    }

    [NativeRequiredFact]
    public void Encode_OfThePhotographsAndAnAlphaRamp_IsTheNativeEncodersFile()
    {
        var images = Directory.EnumerateFiles(Photographs, "*.png").Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), PngDecoder.Decode(File.ReadAllBytes(path))))
            .Append(("alpha ramp", AlphaRamp()));
        foreach (var (name, image) in images)
        {
            using (image)
            {
                var native = NativeReference.EncodeBmp(new NativeReference.Image(image.Pixels.ToArray(), image.Width, image.Height));
                var managed = BmpEncoder.Encode(image);
                managed.AsSpan().SequenceEqual(native).Should().BeTrue($"{name}: the managed file is {managed.Length} B, the native {native.Length} B");

                using var stream = new MemoryStream();
                BmpEncoder.Write(image, stream);
                stream.ToArray().AsSpan().SequenceEqual(native).Should().BeTrue($"{name}: written to a stream");
            }
        }
    }

    // Run-length streams the suite does not reach, each on a 6 x 4 image (RLE8, then RLE4), where 0, 1 and 2
    // after a zero are end of row, end of file and a delta, and a zero then n >= 3 is n literal indices.
    public static TheoryData<string, bool, byte[]> RunLengthStreams() => new()
    {
        { "an RLE8 run longer than its row", false, [9, 1, 0, 0, 2, 2, 0, 1] },
        { "an RLE8 run past the row's end after a delta", false, [2, 1, 0, 2, 3, 0, 5, 2, 0, 0, 1, 3, 0, 1] },
        { "a delta up two rows, then a run", false, [0, 2, 1, 2, 3, 1, 0, 0, 2, 2, 0, 1] },
        { "an end of file in the first row", false, [3, 1, 0, 1] },
        { "an absolute run of three, padded", false, [0, 3, 1, 2, 3, 0, 0, 0, 6, 2, 0, 1] },
        { "an absolute run past the row's end", false, [0, 3, 1, 2, 3, 0, 0, 4, 1, 2, 3, 1, 0, 0] },
        { "a delta below the last row", false, [0, 2, 0, 4, 0, 1] },
        { "a stream that stops without an end of file", false, [6, 1, 0, 0] },
        { "an RLE4 run of alternating nibbles", true, [5, 0x12, 0, 0, 6, 0x33, 0, 1] },
        { "an RLE4 run longer than its row", true, [7, 0x12, 0, 1] },
        { "an RLE4 absolute run of five", true, [0, 5, 0x12, 0x34, 0x50, 0, 0, 0, 0, 1] },
        { "an RLE4 delta, then a run", true, [0, 2, 2, 1, 3, 0x21, 0, 1] },
    };

    [NativeRequiredTheory]
    [MemberData(nameof(RunLengthStreams))]
    public void Decode_OfARunLengthStream_ReadsWhatTheNativeDecoderRead(string what, bool four, byte[] stream)
    {
        var encoded = RunLengthFile(four, stream);
        NativeReference.Image? native;
        try
        {
            native = NativeReference.Decode(encoded);
        }
        catch (ImagingException)
        {
            native = null;
        }

        if (native is null)
        {
            Assert.Throws<ImageDecodeException>(() => BmpDecoder.Decode(encoded).Dispose());
            return;
        }

        using var managed = BmpDecoder.Decode(encoded);
        managed.Pixels.SequenceEqual(native.Value.Rgba).Should().BeTrue($"{what}: the pixels differ");
    }

    // A 6 x 4 bottom-up BMP with an info header, a palette whose entry i is (i * 16, 255 - i * 16, i * 8), and
    // the stream as its pixel data.
    private static byte[] RunLengthFile(bool four, byte[] stream)
    {
        var colours = four ? 16 : 256;
        var offset = 14 + 40 + colours * 4;
        var file = new byte[offset + stream.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(offset).CopyTo(file, 10);
        BitConverter.GetBytes(40).CopyTo(file, 14);
        BitConverter.GetBytes(6).CopyTo(file, 18);
        BitConverter.GetBytes(4).CopyTo(file, 22);
        BitConverter.GetBytes((short)1).CopyTo(file, 26);
        BitConverter.GetBytes((short)(four ? 4 : 8)).CopyTo(file, 28);
        BitConverter.GetBytes(four ? 2 : 1).CopyTo(file, 30);
        BitConverter.GetBytes(stream.Length).CopyTo(file, 34);
        for (var i = 0; i < colours; i++)
        {
            file[54 + i * 4] = (byte)(i * 8);
            file[54 + i * 4 + 1] = (byte)(255 - i * 16);
            file[54 + i * 4 + 2] = (byte)(i * 16);
        }

        stream.CopyTo(file, offset);
        return file;
    }

    // Every alpha from 0 to 255, over colours that change with it: 17 x 15, so rows are not a power of two wide.
    private static PixelBuffer AlphaRamp()
    {
        var image = new PixelBuffer(17, 15);
        var pixels = image.Pixels;
        for (var i = 0; i < 17 * 15; i++)
        {
            pixels[i * 4] = (byte)(i * 7);
            pixels[i * 4 + 1] = (byte)(255 - i);
            pixels[i * 4 + 2] = (byte)(i * 13);
            pixels[i * 4 + 3] = (byte)i;
        }

        return image;
    }
}
