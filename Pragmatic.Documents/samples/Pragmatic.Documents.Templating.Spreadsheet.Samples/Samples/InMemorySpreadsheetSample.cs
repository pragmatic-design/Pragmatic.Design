using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Spreadsheet;

namespace Pragmatic.Documents.Templating.Spreadsheet.Samples.Samples;

/// <summary>
///     Register an in-memory SpreadsheetModel directly (no file system). Use
///     this when the data is already materialized in process — e.g. assembled
///     from an API call, a query result, or another sample stage — and you
///     want to reuse the same catalog shape without writing a throwaway file.
/// </summary>
public static class InMemorySpreadsheetSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- In-memory SpreadsheetModel as data source ---");

        var model = new SpreadsheetBuilder()
            .Title("Signup funnel — April 2026")
            .Sheet("Funnel", s => s
                .HeaderRow("Step", "Users", "ConversionPct")
                .Row("Landing",       10_000, 1.0)
                .Row("Form started",   6_400, 0.64)
                .Row("Form submitted", 4_900, 0.49)
                .Row("Email verified", 4_100, 0.41)
                .Row("Activated",      3_700, 0.37))
            .Build();

        var catalog = new DataSourceCatalog().AddSpreadsheet("funnel", model);
        var ctx = catalog.ToDataContext();
        var resolved = (SpreadsheetModel)(await ctx.ResolveAsync("funnel"))!;
        var sheet = resolved.Sheets[0];

        Console.WriteLine($"  rows resolved          : {sheet.Rows.Count} (incl. header)");
        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            var cells = sheet.Rows[i].Cells;
            Console.WriteLine(
                $"    {cells[0].Value,-18} {cells[1].Value,6}  {Convert.ToDouble(cells[2].Value),6:P1}");
        }
        Console.WriteLine();
    }
}
