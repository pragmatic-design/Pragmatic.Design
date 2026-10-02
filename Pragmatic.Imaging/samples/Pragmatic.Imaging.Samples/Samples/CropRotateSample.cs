using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Spatial transforms: crop, rotate (90/180/270 degrees), flip.
///     These modify geometry without touching colors and compose with filters.
/// </summary>
public static class CropRotateSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Crop / rotate / flip ---");

        // Start from an upscaled QR so cropping produces a visually meaningful output.
        var source = ImagePipeline
            .Load(QrCode.GeneratePng("crop-rotate-demo"))
            .Resize(400, 400)
            .Encode(ImageFormat.Png);

        // Crop to the top-left quadrant (200x200 starting at 0,0).
        var cropped = ImagePipeline.Load(source)
            .Crop(x: 0, y: 0, width: 200, height: 200)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "xform-crop.png"), cropped);
        Console.WriteLine($"  xform-crop.png        {cropped.Length} bytes (200x200 from top-left)");

        var rotated90 = ImagePipeline.Load(source)
            .Rotate(90)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "xform-rotate90.png"), rotated90);
        Console.WriteLine($"  xform-rotate90.png    {rotated90.Length} bytes");

        var flippedH = ImagePipeline.Load(source)
            .FlipHorizontal()
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "xform-fliph.png"), flippedH);
        Console.WriteLine($"  xform-fliph.png       {flippedH.Length} bytes");

        // Compose: flip, rotate, crop, re-encode.
        var composed = ImagePipeline.Load(source)
            .FlipHorizontal()
            .Rotate(180)
            .Crop(100, 100, 200, 200)
            .Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "xform-composed.png"), composed);
        Console.WriteLine($"  xform-composed.png    {composed.Length} bytes (flipH + rot180 + crop)");

        Console.WriteLine();
    }
}
