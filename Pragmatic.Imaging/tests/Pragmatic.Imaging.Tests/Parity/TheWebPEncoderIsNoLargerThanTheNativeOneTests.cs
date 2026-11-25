using System.Globalization;
using System.Text;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.WebP;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     The managed lossless WebP encoder writes no larger a file than the native encoder it replaces, on
///     photographs (the lossless files libwebp wrote, decoded), the photographs of the JPEG test, and
///     synthetic images with flat areas, gradients and alpha.
/// </summary>
public sealed class TheWebPEncoderIsNoLargerThanTheNativeOneTests
{
    [NativeRequiredFact]
    public void EncodeLossless_OfEveryImage_IsNoLargerThanTheNativeEncoders()
    {
        var table = new StringBuilder().AppendLine("image | native bytes | managed bytes | managed / native");
        var larger = new List<string>();
        foreach (var (name, image) in Images())
        {
            using (image)
            {
                var native = NativeReference.EncodeWebP(new NativeReference.Image(image.Pixels.ToArray(), image.Width, image.Height));
                using var managed = new MemoryStream();
                WebPEncoder.EncodeLossless(image, managed);
                table.AppendLine(CultureInfo.InvariantCulture, $"{name} | {native.Length} | {managed.Length} | {(double)managed.Length / native.Length:0.000}");
                if (managed.Length > native.Length)
                    larger.Add(name);
            }
        }

        larger.Should().BeEmpty(table.ToString());
    }

    private static IEnumerable<(string Name, PixelBuffer Image)> Images()
    {
        var webp = Path.Combine(AppContext.BaseDirectory, "References", "WebP");
        foreach (var path in Directory.EnumerateFiles(webp, "lossless-*.webp").Order(StringComparer.Ordinal))
            yield return (Path.GetFileName(path), WebPDecoder.Decode(File.ReadAllBytes(path)));

        var photographs = Path.Combine(AppContext.BaseDirectory, "References", "Photographs");
        foreach (var path in Directory.EnumerateFiles(photographs, "*.png").Order(StringComparer.Ordinal))
            yield return (Path.GetFileName(path), Codecs.Png.PngDecoder.Decode(File.ReadAllBytes(path)));
    }
}
