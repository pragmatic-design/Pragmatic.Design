using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Tests.Parity;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;
using SkiaSharp;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     The managed JPEG encoder, at each quality setting, measured on photographs against their source: what
///     an independent decoder (SkiaSharp, libjpeg-turbo) reads back from its file, compared with the source
///     by SSIM of luma, PSNR and mean CIEDE2000, beside the native encoder it replaced.
/// </summary>
/// <remarks>
///     <para>
///         The photographs are in <c>References/Photographs</c>, with their licences in its manifest. Each
///         quality holds the managed encoder to a floor of SSIM and a ceiling of mean CIEDE2000 on every
///         photograph, set a little outside what it measured (2026-11-25, on these four photographs: SSIM
///         0.9123 to 0.9976, CIEDE2000 1.16 to 3.09). And it may not be worse than the native encoder by
///         SSIM, at the same setting (within <see cref="SameSettingSlack" />) or at the same file size,
///         the native one at the highest quality whose file is no larger. A failure carries the whole table.
///     </para>
///     <para>
///         What this does not hold: colour against the native encoder. The managed encoder subsamples
///         chroma 4:2:0 at every quality, the native one keeps 4:4:4, so at the same size the native one
///         renders colour edges closer to the source from quality 85 up (CIEDE2000 1.52 against 1.34 on the
///         astronaut at 95). Which to write at a high setting is a choice for the encoder: #1018.
///     </para>
/// </remarks>
public sealed class TheJpegEncoderHoldsItsQualityTests
{
    private const double SameSettingSlack = 0.002;

    private static readonly string Photographs = Path.Combine(AppContext.BaseDirectory, "References", "Photographs");

    [Fact]
    public void ThePhotographs_AreEachListedWithTheirProvenance() => ReferenceManifest.AssertComplete(Photographs);

    [NativeRequiredFact]
    public void Quality50_OnEveryPhotograph_HoldsItsThresholds() => Hold(50, minSsim: 0.905, maxDeltaE: 3.2);

    [NativeRequiredFact]
    public void Quality75_OnEveryPhotograph_HoldsItsThresholds() => Hold(75, minSsim: 0.94, maxDeltaE: 2.7);

    [NativeRequiredFact]
    public void Quality85_OnEveryPhotograph_HoldsItsThresholds() => Hold(85, minSsim: 0.958, maxDeltaE: 2.4);

    [NativeRequiredFact]
    public void Quality95_OnEveryPhotograph_HoldsItsThresholds() => Hold(95, minSsim: 0.983, maxDeltaE: 2.0);

    private static void Hold(byte quality, double minSsim, double maxDeltaE)
    {
        var table = new StringBuilder().AppendLine(CultureInfo.InvariantCulture,
            $"quality {quality}: photograph | managed SSIM, PSNR dB, mean dE2000, bytes | native at the same setting | native at the managed size: quality, SSIM, PSNR, dE, bytes");
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Photographs, "*.png").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var source = Decode(File.ReadAllBytes(path));
            var managed = EncodeManaged(source, quality);
            var native = NativeReference.EncodeJpeg(new NativeReference.Image(source.Pixels, source.Width, source.Height), quality);
            var (sizeQuality, sameSize) = NativeAtSize(source, managed.Length);
            var m = Measure(managed, source);
            var n = Measure(native, source);
            var s = Measure(sameSize, source);
            table.AppendLine(CultureInfo.InvariantCulture,
                $"  {name} | {m.Ssim:0.0000}, {m.Psnr:0.00}, {m.DeltaE:0.00}, {managed.Length} | {n.Ssim:0.0000}, {n.Psnr:0.00}, {n.DeltaE:0.00}, {native.Length}" +
                $" | q{sizeQuality}, {s.Ssim:0.0000}, {s.Psnr:0.00}, {s.DeltaE:0.00}, {sameSize.Length}");

            if (m.Ssim < minSsim)
                failures.Add($"{name}: SSIM {m.Ssim:0.0000} under the floor {minSsim}");
            if (m.DeltaE > maxDeltaE)
                failures.Add($"{name}: mean CIEDE2000 {m.DeltaE:0.00} over the ceiling {maxDeltaE}");
            if (m.Ssim < n.Ssim - SameSettingSlack)
                failures.Add($"{name}: SSIM {m.Ssim:0.0000} more than {SameSettingSlack} under the native encoder's {n.Ssim:0.0000} at the same setting");
            if (m.Ssim < s.Ssim)
                failures.Add($"{name}: SSIM {m.Ssim:0.0000} under the native encoder's {s.Ssim:0.0000} at the same size");
        }

        failures.Should().BeEmpty(table.ToString());
    }

    private static (double Ssim, double Psnr, double DeltaE) Measure(byte[] encoded, RgbaImage source)
    {
        var decoded = Decode(encoded);
        return (Ssim.Luma(decoded, source), Psnr.Rgb(decoded, source), DeltaE2000.Images(decoded, source).Mean);
    }

    /// <summary>The native encoder at the highest quality whose file is no larger than <paramref name="bytes" />.</summary>
    private static (byte Quality, byte[] Encoded) NativeAtSize(RgbaImage source, int bytes)
    {
        var image = new NativeReference.Image(source.Pixels, source.Width, source.Height);
        (byte, byte[]) best = (1, NativeReference.EncodeJpeg(image, 1));
        byte low = 2, high = 100;
        while (low <= high)
        {
            var middle = (byte)((low + high) / 2);
            var encoded = NativeReference.EncodeJpeg(image, middle);
            if (encoded.Length <= bytes)
            {
                best = (middle, encoded);
                low = (byte)(middle + 1);
            }
            else
            {
                high = (byte)(middle - 1);
            }
        }

        return best;
    }

    private static byte[] EncodeManaged(RgbaImage source, int quality)
    {
        using var buffer = new PixelBuffer(source.Width, source.Height);
        source.Pixels.CopyTo(buffer.Pixels);
        using var output = new MemoryStream();
        JpegEncoder.Write(buffer, output, quality);
        return output.ToArray();
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
