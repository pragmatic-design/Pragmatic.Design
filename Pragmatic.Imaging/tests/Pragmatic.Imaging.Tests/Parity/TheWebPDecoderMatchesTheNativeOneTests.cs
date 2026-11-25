using System.Globalization;
using System.Text;
using Pragmatic.Imaging.Codecs.WebP;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     The managed WebP decoder against the native one it replaces, on every file libwebp wrote for the
///     codec tests (where the managed decoder is also held to libwebp's own pixels, exactly).
/// </summary>
public sealed class TheWebPDecoderMatchesTheNativeOneTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "References", "WebP");

    [NativeRequiredFact]
    public void Decode_OfEveryFile_MatchesTheNativeDecoder()
    {
        var table = new StringBuilder().AppendLine("file | largest difference per channel R, G, B, A | mean absolute R, G, B | PSNR dB");
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Files, "*.webp").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var encoded = File.ReadAllBytes(path);
            using var managed = WebPDecoder.Decode(encoded);
            var native = NativeReference.Decode(encoded);
            var actual = new RgbaImage(managed.Width, managed.Height, managed.Pixels.ToArray());
            var expected = new RgbaImage(native.Width, native.Height, native.Rgba);
            var difference = PixelDifference.Measure(actual, expected);
            var psnr = Psnr.Rgb(actual, expected);
            table.AppendLine(CultureInfo.InvariantCulture,
                $"{name} | {string.Join(", ", difference.MaxAbsolute)} | {difference.MeanAbsolute[0]:0.000}, {difference.MeanAbsolute[1]:0.000}, {difference.MeanAbsolute[2]:0.000} | {psnr:0.00}");
            if (difference.MaxAbsolute.Any(d => d != 0))
                failures.Add(name);
        }

        failures.Should().BeEmpty(table.ToString());
    }
}
