using Pragmatic.Documents.Markup.Samples.Samples;

Console.WriteLine("=== Pragmatic.Documents.Markup Samples ===\n");

// Pipeline:
//   templates/*.pdxdoc (embedded in this assembly)
//     -> IPdxTemplates.DocumentAsync(name, culture, data)   (Markup: find, parse, resolve)
//       -> ComposedDocument.Model                           (Model)
//         -> PdfRenderer.Render                             (Pdf)
//           -> byte[] PDF
//
// Self-contained: no external infra, no network. The templates live in files, as they do in an
// application, and are found by name; SampleTemplates is the one-time wiring.

var outputDir = Path.Combine(Path.GetTempPath(), "pragmatic-markup-sample");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output directory: {outputDir}\n");

await using var services = SampleTemplates.Build();

await SimpleMarkupSample.Run(services, outputDir);
await DataBoundMarkupSample.Run(services, outputDir);
await BatchPageSample.Run(services, outputDir);

Console.WriteLine("\n=== All samples completed. Inspect the .pdf files in the output directory. ===");
