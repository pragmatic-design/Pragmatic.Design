using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Xlsx.Samples.Samples;

/// <summary>
///     Column widths, custom cell styles, number formats, and styled rows via
///     StyledRow(). Cells built directly (not through Row(params)) can carry
///     per-cell styles — fonts, colors, alignment, number format, borders.
/// </summary>
public static class StylingAndColumnsSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Custom column widths + per-cell styling ---");

        var currencyStyle = new CellStyle
        {
            NumberFormat = "#,##0.00 \u20AC",       // e.g. 1.234,56 €
            HorizontalAlign = HorizontalAlign.Right,
        };
        var percentStyle = new CellStyle
        {
            NumberFormat = "0.00%",
            HorizontalAlign = HorizontalAlign.Right,
        };
        var positiveHighlight = new CellStyle
        {
            Bold = true,
            FontColor = "2A7F3E",     // green
            BackgroundColor = "E8F5EC",
            HorizontalAlign = HorizontalAlign.Right,
            NumberFormat = "#,##0.00 \u20AC",
        };
        var negativeHighlight = new CellStyle
        {
            Bold = true,
            FontColor = "B00020",     // red
            BackgroundColor = "FBE5E7",
            HorizontalAlign = HorizontalAlign.Right,
            NumberFormat = "#,##0.00 \u20AC",
        };

        var model = new SpreadsheetBuilder()
            .Title("P&L snapshot")
            .Sheet("P&L", s => s
                .Column(width: 28)  // Category
                .Column(width: 20)  // Budget
                .Column(width: 20)  // Actual
                .Column(width: 20)  // Delta
                .Column(width: 14)  // Variance %
                .HeaderRow("Category", "Budget", "Actual", "Delta", "Variance")
                .StyledRow(
                    new Cell { Value = "Rent" },
                    new Cell { Value = 9000.00, Style = currencyStyle },
                    new Cell { Value = 8850.00, Style = currencyStyle },
                    new Cell { Value =  -150.00, Style = positiveHighlight },
                    new Cell { Value = -0.0167, Style = percentStyle })
                .StyledRow(
                    new Cell { Value = "Marketing" },
                    new Cell { Value =  6000.00, Style = currencyStyle },
                    new Cell { Value =  7420.00, Style = currencyStyle },
                    new Cell { Value =  1420.00, Style = negativeHighlight },
                    new Cell { Value =  0.2367, Style = percentStyle })
                .StyledRow(
                    new Cell { Value = "Travel" },
                    new Cell { Value =  2000.00, Style = currencyStyle },
                    new Cell { Value =  1870.00, Style = currencyStyle },
                    new Cell { Value =  -130.00, Style = positiveHighlight },
                    new Cell { Value = -0.0650, Style = percentStyle }))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var path = Path.Combine(outputDir, "pnl-snapshot.xlsx");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  pnl-snapshot.xlsx      {bytes.Length} bytes (custom styles + column widths)");
        Console.WriteLine();
    }
}
