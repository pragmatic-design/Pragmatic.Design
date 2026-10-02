using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Apply color/blur/sharpen/brightness filters via the fluent pipeline.
///     Every filter returns a new ImagePipeline, so operations compose cleanly.
/// </summary>
public static class FiltersSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Filters (grayscale / blur / sharpen / brightness) ---");

        var source = QrCode.GeneratePng("filters-demo", moduleSize: 10);

        var grayscale = ImagePipeline.Load(source)
            .Grayscale()
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "filter-grayscale.png"), grayscale);
        Console.WriteLine($"  filter-grayscale.png  {grayscale.Length} bytes");

        var blurred = ImagePipeline.Load(source)
            .Blur(sigma: 2.5f)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "filter-blur.png"), blurred);
        Console.WriteLine($"  filter-blur.png       {blurred.Length} bytes (sigma=2.5)");

        var sharpened = ImagePipeline.Load(source)
            .Sharpen(sigma: 1.0f, threshold: 2)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "filter-sharpen.png"), sharpened);
        Console.WriteLine($"  filter-sharpen.png    {sharpened.Length} bytes");

        var brighter = ImagePipeline.Load(source)
            .Brightness(value: 40)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "filter-brighter.png"), brighter);
        Console.WriteLine($"  filter-brighter.png   {brighter.Length} bytes (+40)");

        // Filters chain: brighten then convert to grayscale.
        var chained = ImagePipeline.Load(source)
            .Brightness(20)
            .Grayscale()
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "filter-chained.png"), chained);
        Console.WriteLine($"  filter-chained.png    {chained.Length} bytes (bright + grayscale)");

        Console.WriteLine();
    }
}
