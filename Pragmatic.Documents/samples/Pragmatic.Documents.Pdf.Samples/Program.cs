using Pragmatic.Documents.Pdf.Samples.Samples;

Console.WriteLine("=== Pragmatic.Documents.Pdf Samples ===\n");

// Pipeline: a .pdxdoc template through IPdxTemplates (the recommended way, PdxTemplateSample), or
// DocumentBuilder in code (the rest) -> DocumentModel -> PdfRenderer.Render -> byte[].
// PdfRenderer is pure static — no DI, no infrastructure, no external deps.
// Each sample writes its output under %TEMP%/pragmatic-pdf-sample/<name>.pdf
// so you can open the generated documents in a PDF viewer.

var outputDir = Path.Combine(Path.GetTempPath(), "pragmatic-pdf-sample");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output directory: {outputDir}\n");

await PdxTemplateSample.Run(outputDir);
BasicPdfSample.Run(outputDir);
TableAndListSample.Run(outputDir);
MultiPageSample.Run(outputDir);
BarcodeAndLandscapeSample.Run(outputDir);
PdfPageSetupSample.Run(outputDir);
await PdfBatchSample.Run(outputDir);
await PdfOperationsSample.Run(outputDir);

Console.WriteLine("\n=== All samples completed. Inspect the .pdf files in the output directory. ===");
