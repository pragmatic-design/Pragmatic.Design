using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Spreadsheet;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Templating.Spreadsheet.Samples.Samples;

/// <summary>
///     Register an xlsx file on disk as a named DataSource. Same shape as the
///     CSV sample, but the fixture is a binary spreadsheet written through
///     SpreadsheetBuilder + XlsxRenderer. Mirrors how a scheduled job would
///     consume an Excel file maintained by non-technical users.
/// </summary>
public static class XlsxFileDataSourceSample
{
    public static async Task Run(string fixtureDir)
    {
        Console.WriteLine("--- Xlsx file as data source ---");

        // 1. Build + write a fixture xlsx file.
        var fixtureModel = new SpreadsheetBuilder()
            .Title("Q1 revenue snapshot")
            .Sheet("Q1", s => s
                .HeaderRow("Channel", "Orders", "Revenue")
                .Row("web",    595, 77_510.50)
                .Row("mobile", 372, 47_950.95)
                .Row("store",  128, 19_220.00))
            .Build();
        var xlsxPath = Path.Combine(fixtureDir, "q1-revenue.xlsx");
        await File.WriteAllBytesAsync(xlsxPath, XlsxRenderer.Render(fixtureModel));
        Console.WriteLine($"  fixture written        : {xlsxPath}");

        // 2. Register the file as a data source.
        var catalog = new DataSourceCatalog()
            .AddXlsxFile("revenue", xlsxPath);

        // 3. Resolve and iterate.
        var ctx = catalog.ToDataContext();
        var resolved = (SpreadsheetModel)(await ctx.ResolveAsync("revenue"))!;
        var sheet = resolved.Sheets[0];

        Console.WriteLine($"  sheet name             : {sheet.Name}");
        Console.WriteLine($"  resolved rows          : {sheet.Rows.Count} (incl. header)");
        // Cell values reflect whatever the xlsx reader extracted — XLSX stores
        // numbers in a shared cell table and the reader surfaces them as
        // strings or doubles depending on the cell's style. This sample prints
        // them verbatim to keep the data shape visible; downstream consumers
        // usually cast through CultureInfo-aware parsing.
        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            var cells = sheet.Rows[i].Cells;
            Console.WriteLine($"    - {cells[0].Value,-8} {cells[1].Value,6} orders  {cells[2].Value}");
        }
        Console.WriteLine();
    }
}
