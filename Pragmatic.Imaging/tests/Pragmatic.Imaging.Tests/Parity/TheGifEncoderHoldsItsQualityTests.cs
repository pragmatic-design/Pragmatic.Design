using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Gif;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;
using SkiaSharp;
using Xunit.Abstractions;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     Where the managed GIF encoder cannot write the native file, on a photograph of more than 256 colours, its
///     palette (Wu's, against the native library's NeuQuant) is held to the native quality: what an independent
///     decoder (SkiaSharp) reads back from each file, compared with the source by SSIM of luma, PSNR and mean
///     CIEDE2000.
/// </summary>
public sealed class TheGifEncoderHoldsItsQualityTests(ITestOutputHelper output)
{
    private const double SsimSlack = 0.002;

    private static readonly string Photographs = Path.Combine(AppContext.BaseDirectory, "References", "Photographs");

    [NativeRequiredFact]
    public void Encode_OfEveryPhotograph_IsNoWorseThanTheNativeEncoder()
    {
        var table = new StringBuilder().AppendLine(
            "photograph | managed SSIM, PSNR dB, mean dE2000, bytes | dithered SSIM, PSNR, dE, bytes | native SSIM, PSNR, dE, bytes");
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Photographs, "*.png").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var source = Decode(File.ReadAllBytes(path));
            using var buffer = new PixelBuffer(source.Width, source.Height);
            source.Pixels.CopyTo(buffer.Pixels);
            var managed = GifEncoder.Encode(buffer);
            var dithered = GifEncoder.Encode(buffer, dither: true);
            var native = NativeReference.EncodeGif(new NativeReference.Image(source.Pixels, source.Width, source.Height));
            var m = Measure(managed, source);
            var d = Measure(dithered, source);
            var n = Measure(native, source);
            table.AppendLine(CultureInfo.InvariantCulture,
                $"  {name} | {m.Ssim:0.0000}, {m.Psnr:0.00}, {m.DeltaE:0.00}, {managed.Length} | {d.Ssim:0.0000}, {d.Psnr:0.00}, {d.DeltaE:0.00}, {dithered.Length}" +
                $" | {n.Ssim:0.0000}, {n.Psnr:0.00}, {n.DeltaE:0.00}, {native.Length}");
            if (m.Ssim < n.Ssim - SsimSlack)
                failures.Add($"{name}: SSIM {m.Ssim:0.0000} more than {SsimSlack} under the native {n.Ssim:0.0000}");
            if (m.DeltaE > n.DeltaE)
                failures.Add($"{name}: mean dE2000 {m.DeltaE:0.00} over the native {n.DeltaE:0.00}");
        }

        output.WriteLine(table.ToString());
        failures.Should().BeEmpty(table.ToString());
    }

    private static (double Ssim, double Psnr, double DeltaE) Measure(byte[] encoded, RgbaImage source)
    {
        var decoded = Decode(encoded);
        return (Ssim.Luma(decoded, source), Psnr.Rgb(decoded, source), DeltaE2000.Images(decoded, source).Mean);
    }

    /// <summary>SkiaSharp's decoder, to unpremultiplied RGBA: independent of both encoders.</summary>
    private static RgbaImage Decode(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("Skia cannot read the bytes.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidOperationException($"Skia decode: {result}.");

        var pixels = new byte[info.BytesSize];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new RgbaImage(info.Width, info.Height, pixels);
    }
}
