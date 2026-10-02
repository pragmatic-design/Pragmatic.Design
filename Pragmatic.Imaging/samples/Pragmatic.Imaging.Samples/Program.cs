using Pragmatic.Imaging.Samples.Samples;

Console.WriteLine("=== Pragmatic.Imaging Samples ===\n");

// Pragmatic.Imaging binds to a Rust native library (pragmatic_imaging_native).
// win-x64 binaries ship in the package; Linux/macOS require cross-compilation
// (see module README). If the native library isn't found on your platform,
// each sample below throws on its first Pragmatic.Imaging call.

var outputDir = Path.Combine(Path.GetTempPath(), "pragmatic-imaging-sample");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output directory: {outputDir}\n");

QrCodeSample.Run(outputDir);
ImageInfoSample.Run(outputDir);
ResizeAndFormatSample.Run(outputDir);
FiltersSample.Run(outputDir);
CropRotateSample.Run(outputDir);
await ConverterOneLinersSample.RunAsync(outputDir);
await BatchProcessingSample.RunAsync(outputDir);
ImagingOptionsSample.Run(outputDir);
AvifTiffEncodeSample.Run(outputDir);
DependencyInjectionSample.Run(outputDir);

Console.WriteLine("\n=== All samples completed. Inspect files in the output directory. ===");
