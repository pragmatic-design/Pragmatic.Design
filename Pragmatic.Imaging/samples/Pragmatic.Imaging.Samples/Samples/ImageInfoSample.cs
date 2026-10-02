using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Read dimensions + format from image bytes without a full decode.
///     Cheap probe: useful for validating uploads before paying for the decode cost.
/// </summary>
public static class ImageInfoSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Image info probe (no full decode) ---");

        // Reuse the QR we wrote in the previous sample as a known-good input.
        var qrPath = Path.Combine(outputDir, "qr-default.png");
        var qrBytes = File.ReadAllBytes(qrPath);

        var info = ImageInfo.FromBytes(qrBytes);
        Console.WriteLine($"  qr-default.png        {info.Width}x{info.Height} {info.Format}");

        // Any other PNG works the same way. Here we generate a denser QR to vary the size.
        var dense = QrCode.GeneratePng("the-quick-brown-fox-jumps-over-the-lazy-dog", moduleSize: 4);
        var denseInfo = ImageInfo.FromBytes(dense);
        Console.WriteLine($"  in-memory dense QR    {denseInfo.Width}x{denseInfo.Height} {denseInfo.Format}");

        Console.WriteLine();
    }
}
