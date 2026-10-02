using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Safety limits via <see cref="ImagingOptions"/>. These guard against decompression bombs
///     and runaway memory by capping input size and decoded megapixels. The sample shows the
///     three presets (Default / Strict / Relaxed), passing options into a Load call, and a Strict
///     limit rejecting an over-budget input with an <see cref="ImagingException"/>.
/// </summary>
public static class ImagingOptionsSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- ImagingOptions (safety limits / presets) ---");

        // Presets ship ready to use. Print their limits so the trade-offs are visible.
        Print("Default", ImagingOptions.Default);
        Print("Strict ", ImagingOptions.Strict);
        Print("Relaxed", ImagingOptions.Relaxed);

        var source = QrCode.GeneratePng("imaging-options-demo", moduleSize: 8);

        // Pass options into Load. Strict is the right choice for user-uploaded content.
        using (var pipeline = ImagePipeline.Load(source, ImagingOptions.Strict))
        {
            var encoded = pipeline.Encode(ImageFormat.Png);
            File.WriteAllBytes(Path.Combine(outputDir, "options-strict.png"), encoded);
            Console.WriteLine($"  options-strict.png    {encoded.Length} bytes (loaded under Strict limits)");
        }

        // Custom options: records support `with`-style init. Here a tiny input cap that our
        // source deliberately exceeds, to demonstrate the rejection path.
        var tinyCap = new ImagingOptions { MaxInputBytes = 16, MaxMegapixels = 1 };
        Console.WriteLine($"  custom cap            MaxInputBytes={tinyCap.MaxInputBytes}, source={source.Length} bytes");

        try
        {
            using var rejected = ImagePipeline.Load(source, tinyCap);
            Console.WriteLine("  unexpected            load should have been rejected");
        }
        catch (ImagingException ex)
        {
            Console.WriteLine($"  rejected as expected  {ex.Message}");
        }

        Console.WriteLine();
    }

    private static void Print(string label, ImagingOptions options)
        => Console.WriteLine(
            $"  {label}              MaxInputBytes={options.MaxInputBytes / (1024 * 1024)} MB, MaxMegapixels={options.MaxMegapixels} MP");
}
