using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Generate PNG-encoded QR codes synchronously and asynchronously.
///     Useful for invoice footers, login links, short URLs, anything text-to-image.
/// </summary>
public static class QrCodeSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- QR code generation ---");

        // Default module size (10px) + 2-module quiet margin. Good for print + screen.
        var defaultPng = QrCode.GeneratePng("https://www.pragmaticdesign.net");
        File.WriteAllBytes(Path.Combine(outputDir, "qr-default.png"), defaultPng);
        Console.WriteLine($"  qr-default.png        {defaultPng.Length} bytes");

        // Larger modules + wider margin — suitable for physical signage.
        var largePng = QrCode.GeneratePng(
            text: "https://github.com/pragmatic-design/Pragmatic.Design",
            moduleSize: 16,
            margin: 4);
        File.WriteAllBytes(Path.Combine(outputDir, "qr-large.png"), largePng);
        Console.WriteLine($"  qr-large.png          {largePng.Length} bytes (modules=16, margin=4)");

        // Async variant — swap in when generating many QRs in a request pipeline.
        var asyncPng = QrCode.GeneratePngAsync("PREVIEW-0").GetAwaiter().GetResult();
        File.WriteAllBytes(Path.Combine(outputDir, "qr-async.png"), asyncPng);
        Console.WriteLine($"  qr-async.png          {asyncPng.Length} bytes (async API)");

        Console.WriteLine();
    }
}
