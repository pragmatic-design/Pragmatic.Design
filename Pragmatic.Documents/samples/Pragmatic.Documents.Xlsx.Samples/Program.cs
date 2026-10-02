using Pragmatic.Documents.Xlsx.Samples.Samples;

Console.WriteLine("=== Pragmatic.Documents.Xlsx Samples ===\n");

// Pipeline: SpreadsheetBuilder -> SpreadsheetModel -> XlsxRenderer.Render -> byte[].
// XlsxRenderer is pure static — no DI, no external infra. Output files land
// under %TEMP%/pragmatic-xlsx-sample/ and open natively in Excel, Numbers,
// LibreOffice Calc, and Google Sheets.

var outputDir = Path.Combine(Path.GetTempPath(), "pragmatic-xlsx-sample");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output directory: {outputDir}\n");

BasicSpreadsheetSample.Run(outputDir);
MultipleSheetsSample.Run(outputDir);
FormulasAndFreezeSample.Run(outputDir);
StylingAndColumnsSample.Run(outputDir);

Console.WriteLine("\n=== All samples completed. Open the .xlsx files to inspect. ===");
