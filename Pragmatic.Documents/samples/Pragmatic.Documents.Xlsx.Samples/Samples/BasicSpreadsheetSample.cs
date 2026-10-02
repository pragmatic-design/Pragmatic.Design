using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Xlsx.Samples.Samples;

/// <summary>
///     Smallest possible xlsx: a single sheet with a header row and data rows.
///     HeaderRow auto-applies bold styling; Row() accepts mixed CLR types
///     (string, int, double, decimal, DateTime, bool, null) and each cell is
///     typed accordingly in the output.
/// </summary>
public static class BasicSpreadsheetSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Basic spreadsheet (one sheet, typed cells) ---");

        var model = new SpreadsheetBuilder()
            .Title("Customer export")
            .Author("Pragmatic.Design")
            .Sheet("Customers", s => s
                .HeaderRow("Id", "Name", "Email", "SignupDate", "LifetimeValue", "Active")
                .Row(1,  "Alice Example", "alice@example.com",  new DateTime(2026, 1, 14), 1234.50, true)
                .Row(2,  "Bob Example",   "bob@example.com",    new DateTime(2026, 2, 3),   180.00, true)
                .Row(3,  "Carol Example", "carol@example.com",  new DateTime(2026, 3, 27), 4720.90, false)
                .Row(4,  "Dave Example",  "dave@example.com",   new DateTime(2026, 4, 1),    92.10, true))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var path = Path.Combine(outputDir, "customers.xlsx");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  customers.xlsx         {bytes.Length} bytes (1 sheet, 4 rows)");
        Console.WriteLine();
    }
}
