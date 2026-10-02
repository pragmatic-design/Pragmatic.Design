using Pragmatic.Documents.Docx.Samples.Samples;

Console.WriteLine("=== Pragmatic.Documents.Docx Samples ===\n");

// Pipeline: a .pdxdoc template through IPdxTemplates (the recommended way, PdxTemplateSample), or
// DocumentBuilder in code (the rest) -> DocumentModel -> DocxRenderer.Render -> byte[].
// Same DocumentModel used for PDF rendering (Documents.Pdf) — swap renderer
// to change format. Output lands under %TEMP%/pragmatic-docx-sample/<name>.docx
// and opens in Word / LibreOffice / Pages.

var outputDir = Path.Combine(Path.GetTempPath(), "pragmatic-docx-sample");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output directory: {outputDir}\n");

await PdxTemplateSample.Run(outputDir);
BasicDocxSample.Run(outputDir);
TableAndStyledSample.Run(outputDir);
MultiPageWithTocSample.Run(outputDir);
LandscapeAndHyperlinkSample.Run(outputDir);
ThemeAndRenderOptionsSample.Run(outputDir);
FieldFootnoteBookmarkSample.Run(outputDir);

Console.WriteLine("\n=== All samples completed. Open the .docx files in Word / LibreOffice. ===");
