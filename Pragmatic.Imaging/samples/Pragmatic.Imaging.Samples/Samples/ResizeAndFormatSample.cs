using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Fluent pipeline: Load → Resize → Encode. Demonstrates format conversion
///     (PNG → JPEG / WebP) with quality control, and how the filter choice
///     affects resampling output.
/// </summary>
public static class ResizeAndFormatSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Resize + format conversion ---");

        // Start from a QR at its natural size (small). Upscale it so we can see
        // the effect of different resize filters in the output files.
        var source = QrCode.GeneratePng("https://www.pragmaticdesign.net", moduleSize: 8);

        var jpeg = ImagePipeline.Load(source)
            .Resize(400, 400)
            .Encode(ImageFormat.Jpeg, quality: 85);
        File.WriteAllBytes(Path.Combine(outputDir, "qr-400x400.jpg"), jpeg);
        Console.WriteLine($"  qr-400x400.jpg        {jpeg.Length} bytes (JPEG quality 85)");

        var webp = ImagePipeline.Load(source)
            .Resize(400, 400)
            .Encode(ImageFormat.WebP, quality: 80);
        File.WriteAllBytes(Path.Combine(outputDir, "qr-400x400.webp"), webp);
        Console.WriteLine($"  qr-400x400.webp       {webp.Length} bytes (WebP quality 80)");

        // Compare resize filters: Nearest is blocky but fastest; Lanczos3 is smooth.
        var nearest = ImagePipeline.Load(source)
            .Resize(400, 400, ResizeFilter.Nearest)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "qr-400x400-nearest.png"), nearest);
        Console.WriteLine($"  qr-400x400-nearest    {nearest.Length} bytes (PNG, Nearest filter)");

        var lanczos = ImagePipeline.Load(source)
            .Resize(400, 400, ResizeFilter.Lanczos3)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "qr-400x400-lanczos.png"), lanczos);
        Console.WriteLine($"  qr-400x400-lanczos    {lanczos.Length} bytes (PNG, Lanczos3 filter)");

        Console.WriteLine();
    }
}
