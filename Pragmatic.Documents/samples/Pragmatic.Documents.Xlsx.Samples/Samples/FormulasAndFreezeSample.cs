using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Xlsx.Samples.Samples;

/// <summary>
///     Frozen header rows + formula cells + merged cells. The FormulaRow()
///     helper treats any string starting with '=' as a formula expression.
///     Freeze(1, 1) locks the top row and the left column when scrolling.
///     Merge() spans a rectangle of cells.
/// </summary>
public static class FormulasAndFreezeSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Formulas + freeze panes + merged cells ---");

        var model = new SpreadsheetBuilder()
            .Title("Budget tracker")
            .Sheet("2026 budget", s => s
                .Freeze(rows: 1, columns: 1)  // freeze top row + first column
                .HeaderRow("Category", "Q1", "Q2", "Q3", "Q4", "Total")
                .FormulaRow("Rent",        "9000",  "9000",  "9000",  "9000",  "=SUM(B2:E2)")
                .FormulaRow("Salaries",    "48000", "48000", "50000", "50000", "=SUM(B3:E3)")
                .FormulaRow("Marketing",   "6000",  "8000",  "7500",  "9000",  "=SUM(B4:E4)")
                .FormulaRow("Travel",      "2000",  "3500",  "2200",  "2800",  "=SUM(B5:E5)")
                .FormulaRow("Software",    "4200",  "4200",  "4400",  "4400",  "=SUM(B6:E6)")
                .FormulaRow("Total",       "=SUM(B2:B6)", "=SUM(C2:C6)", "=SUM(D2:D6)", "=SUM(E2:E6)", "=SUM(F2:F6)")
                .Merge("A8", "F8")
                .Row("Figures in EUR. Recompute in Excel to resolve totals."))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var path = Path.Combine(outputDir, "budget.xlsx");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  budget.xlsx            {bytes.Length} bytes (freeze + formulas + merge)");
        Console.WriteLine();
    }
}
