using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Encoding to the AVIF and TIFF formats. TIFF is lossless and fully round-trippable
///     (encode + decode). AVIF is encode-only with the shipped native binary: we can produce
///     AVIF bytes, but decoding AVIF back is not supported, so we report that explicitly rather
///     than attempting a round-trip that would fail.
/// </summary>
public static class AvifTiffEncodeSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- AVIF + TIFF encoding ---");

        var source = ImagePipeline
            .Load(QrCode.GeneratePng("avif-tiff-demo", moduleSize: 6))
            .Resize(256, 256)
            .Encode(ImageFormat.Png);

        // TIFF: lossless, flexible. Encode then probe the result to prove it round-trips.
        var tiff = ImagePipeline.Load(source).Encode(ImageFormat.Tiff);
        File.WriteAllBytes(Path.Combine(outputDir, "encode.tiff"), tiff);
        var tiffInfo = ImageInfo.FromBytes(tiff);
        Console.WriteLine($"  encode.tiff           {tiff.Length} bytes (re-probe: {tiffInfo.Width}x{tiffInfo.Height} {tiffInfo.Format})");

        // AVIF: AV1-based, high compression. Encode-only with the shipped binary.
        // quality maps to the AV1 quantizer; lower quality = smaller file.
        var avif = ImagePipeline.Load(source).Encode(ImageFormat.Avif, quality: 60);
        File.WriteAllBytes(Path.Combine(outputDir, "encode.avif"), avif);
        Console.WriteLine($"  encode.avif           {avif.Length} bytes (AVIF quality 60, encode-only)");

        // Demonstrate the encode-only constraint: decoding AVIF is not supported by the
        // shipped binary, so this probe is expected to fail. We handle it instead of crashing.
        try
        {
            var avifInfo = ImageInfo.FromBytes(avif);
            Console.WriteLine($"  avif re-probe         {avifInfo.Width}x{avifInfo.Height} {avifInfo.Format} (decode supported here)");
        }
        catch (ImagingException ex)
        {
            Console.WriteLine($"  avif decode skipped   not supported by shipped binary ({ex.Message})");
        }

        Console.WriteLine();
    }
}
