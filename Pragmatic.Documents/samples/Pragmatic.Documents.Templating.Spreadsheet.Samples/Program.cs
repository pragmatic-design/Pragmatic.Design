using Pragmatic.Documents.Templating.Spreadsheet.Samples.Samples;

Console.WriteLine("=== Pragmatic.Documents.Templating.Spreadsheet Samples ===\n");

// This module turns external spreadsheets (xlsx) and delimited files (csv)
// into named data sources that feed the templating pipeline. Real-world use
// case: your business users maintain data in Excel/Google Sheets; a scheduled
// job reads the file, feeds it to a template, and produces downstream reports.
//
// API surface exercised:
//   DataSourceCatalog                       — registry of named providers
//   .AddCsvFile("name", path, options)      — CSV file as data source
//   .AddXlsxFile("name", path)              — xlsx file as data source
//   .AddCsvStream("name", open)             — CSV from anywhere a stream opens
//   .AddXlsxStream("name", open)            — xlsx, likewise
//   .AddSpreadsheet("name", model)          — in-memory model as data source
//
// ⚠️ The file overloads are for a spreadsheet the OPERATOR puts beside the
// application. One a TENANT uploads has no path — it is behind IFileStorage —
// and that is what the stream overloads are for.
//   .ToDataContext()                         — bridge into TemplateDataContext
//   await ctx.ResolveAsync("name")           — returns the SpreadsheetModel

var fixtureDir = Path.Combine(Path.GetTempPath(), "pragmatic-tmpss-sample");
Directory.CreateDirectory(fixtureDir);
Console.WriteLine($"Fixture directory: {fixtureDir}\n");

await CsvFileDataSourceSample.Run(fixtureDir);
await XlsxFileDataSourceSample.Run(fixtureDir);
await CsvStreamDataSourceSample.Run();
await InMemorySpreadsheetSample.Run();

Console.WriteLine("\n=== All samples completed. ===");
