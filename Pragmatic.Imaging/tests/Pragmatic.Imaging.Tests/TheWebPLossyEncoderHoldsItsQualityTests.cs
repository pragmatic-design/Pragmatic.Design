using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.WebP;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;
using SkiaSharp;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     The managed lossy WebP encoder, at each quality setting, measured on photographs against their source:
///     what an independent decoder (SkiaSharp's libwebp) reads back from its file, compared with the source by
///     SSIM of luma, PSNR and mean CIEDE2000, beside libwebp's own encoder at the same setting.
/// </summary>
/// <remarks>
///     The native library never wrote lossy WebP, so the reference is libwebp, through SkiaSharp. Each quality
///     holds the managed encoder to a floor of SSIM on every photograph, and to libwebp's SSIM at the same
///     setting less <see cref="SameSettingSlack" />. The file sizes of both are in the table a failure carries.
/// </remarks>
public sealed class TheWebPLossyEncoderHoldsItsQualityTests
{
    private const double SameSettingSlack = 0.002;

    private static readonly string Photographs = Path.Combine(AppContext.BaseDirectory, "References", "Photographs");

    [Fact]
    public void Quality50_OnEveryPhotograph_HoldsItsThresholds() => Hold(50, minSsim: 0.90);

    [Fact]
    public void Quality75_OnEveryPhotograph_HoldsItsThresholds() => Hold(75, minSsim: 0.93);

    [Fact]
    public void Quality90_OnEveryPhotograph_HoldsItsThresholds() => Hold(90, minSsim: 0.96);

    private static void Hold(int quality, double minSsim)
    {
        var table = new StringBuilder().AppendLine(CultureInfo.InvariantCulture,
            $"quality {quality}: photograph | managed SSIM, PSNR dB, mean dE2000, bytes | libwebp SSIM, PSNR dB, mean dE2000, bytes | managed / libwebp bytes");
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Photographs, "*.png").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var source = Decode(File.ReadAllBytes(path));
            var managed = EncodeManaged(source, quality);
            var libwebp = EncodeLibWebP(source, quality);
            var m = Measure(managed, source);
            var l = Measure(libwebp, source);
            table.AppendLine(CultureInfo.InvariantCulture,
                $"  {name} | {m.Ssim:0.0000}, {m.Psnr:0.00}, {m.DeltaE:0.00}, {managed.Length} | {l.Ssim:0.0000}, {l.Psnr:0.00}, {l.DeltaE:0.00}, {libwebp.Length}" +
                $" | {(double)managed.Length / libwebp.Length:0.000}");

            if (m.Ssim < minSsim)
                failures.Add($"{name}: SSIM {m.Ssim:0.0000} under the floor {minSsim}");
            if (m.Ssim < l.Ssim - SameSettingSlack)
                failures.Add($"{name}: SSIM {m.Ssim:0.0000} more than {SameSettingSlack} under libwebp's {l.Ssim:0.0000} at the same setting");
        }

        failures.Should().BeEmpty(table.ToString());
    }

    private static (double Ssim, double Psnr, double DeltaE) Measure(byte[] encoded, RgbaImage source)
    {
        var decoded = Decode(encoded);
        return (Ssim.Luma(decoded, source), Psnr.Rgb(decoded, source), DeltaE2000.Images(decoded, source).Mean);
    }

    private static byte[] EncodeManaged(RgbaImage source, int quality)
    {
        using var buffer = new PixelBuffer(source.Width, source.Height);
        source.Pixels.CopyTo(buffer.Pixels);
        return WebPEncoder.EncodeLossy(buffer, quality);
    }

    /// <summary>libwebp's lossy encoder, through SkiaSharp, at the same quality.</summary>
    private static byte[] EncodeLibWebP(RgbaImage source, int quality)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(source.Pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), info.RowBytes);
            using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality))
                             ?? throw new InvalidOperationException("Skia cannot encode WebP.");
            return data.ToArray();
        }
        finally
        {
            handle.Free();
        }
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
