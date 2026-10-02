using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Static one-liner helpers from <see cref="ImageConverter"/>: the fast path when you
///     do not need a full fluent pipeline. Demonstrates <c>ResizeAsync</c>, <c>ConvertAsync</c>,
///     <c>ThumbnailAsync</c> and <c>StripExifAsync</c> — each a single CPU-bound native call
///     offloaded via Task.Run, so they compose naturally in an async request pipeline.
/// </summary>
public static class ConverterOneLinersSample
{
    public static async Task RunAsync(string outputDir)
    {
        Console.WriteLine("--- ImageConverter one-liners (Resize / Convert / Thumbnail / StripExif) ---");

        // A known-good PNG source. QrCode.GeneratePng gives us deterministic bytes.
        var source = QrCode.GeneratePng("converter-one-liners", moduleSize: 8);
        Console.WriteLine($"  source PNG            {source.Length} bytes");

        // ResizeAsync: load → resize to exact dimensions → encode, in one call.
        var resized = await ImageConverter.ResizeAsync(source, width: 256, height: 256, ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "oneliner-resize.png"), resized);
        Console.WriteLine($"  oneliner-resize.png   {resized.Length} bytes (256x256)");

        // ConvertAsync: format conversion only (PNG → JPEG) at a chosen quality.
        var converted = await ImageConverter.ConvertAsync(source, ImageFormat.Jpeg, quality: 80);
        File.WriteAllBytes(Path.Combine(outputDir, "oneliner-convert.jpg"), converted);
        Console.WriteLine($"  oneliner-convert.jpg  {converted.Length} bytes (JPEG quality 80)");

        // ThumbnailAsync: fit-within-bounds while preserving aspect ratio.
        var thumb = await ImageConverter.ThumbnailAsync(source, maxWidth: 96, maxHeight: 96, ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "oneliner-thumb.png"), thumb);
        var thumbInfo = ImageInfo.FromBytes(thumb);
        Console.WriteLine($"  oneliner-thumb.png    {thumb.Length} bytes ({thumbInfo.Width}x{thumbInfo.Height}, fit 96x96)");

        // StripExifAsync: re-encode in the same format to drop metadata.
        // Our QR PNG carries no EXIF, but the call demonstrates the privacy-scrub workflow
        // you would run on user uploads before storing them.
        var stripped = await ImageConverter.StripExifAsync(source, quality: 95);
        File.WriteAllBytes(Path.Combine(outputDir, "oneliner-stripexif.png"), stripped);
        Console.WriteLine($"  oneliner-stripexif    {stripped.Length} bytes (metadata removed, re-encoded)");

        Console.WriteLine();
    }
}
