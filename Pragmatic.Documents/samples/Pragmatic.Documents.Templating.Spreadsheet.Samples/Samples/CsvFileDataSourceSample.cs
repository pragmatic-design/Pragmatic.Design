using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Spreadsheet;

namespace Pragmatic.Documents.Templating.Spreadsheet.Samples.Samples;

/// <summary>
///     Register a CSV file on disk as a named DataSource. The catalog reads the
///     file lazily when the resolver first asks for it and materializes a
///     SpreadsheetModel the templating pipeline can consume.
/// </summary>
public static class CsvFileDataSourceSample
{
    public static async Task Run(string fixtureDir)
    {
        Console.WriteLine("--- CSV file as data source ---");

        // 1. Write a fixture CSV file so the sample is self-contained.
        string[] headers = ["EmployeeId", "Name", "Department", "GrossSalary"];
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "E-001", "Alice Bianchi",   "Engineering", "4500.00" },
            new string?[] { "E-002", "Bruno Costa",     "Sales",       "3800.00" },
            new string?[] { "E-003", "Carla De Luca",   "Support",     "3200.00" },
            new string?[] { "E-004", "Davide Esposito", "Engineering", "4800.00" },
        };
        var csvPath = Path.Combine(fixtureDir, "employees.csv");
        await File.WriteAllBytesAsync(csvPath, CsvWriter.WriteToArray(headers, rows));
        Console.WriteLine($"  fixture written        : {csvPath}");

        // 2. Register as a data source in the catalog.
        var catalog = new DataSourceCatalog()
            .AddCsvFile("employees", csvPath);
        Console.WriteLine($"  HasSource(employees)?  : {catalog.HasSource("employees")}");

        // 3. Resolve the source through the data context.
        var ctx = catalog.ToDataContext();
        var raw = await ctx.ResolveAsync("employees");
        var model = (SpreadsheetModel)raw!;

        // 4. Iterate rows — first row is the header, data starts at index 1.
        var sheet = model.Sheets[0];
        Console.WriteLine($"  resolved rows          : {sheet.Rows.Count} (incl. header)");
        Console.WriteLine($"  columns                : {string.Join(", ", sheet.Rows[0].Cells.Select(c => c.Value))}");
        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            var cells = sheet.Rows[i].Cells;
            Console.WriteLine(
                $"    - {cells[0].Value,-6} {cells[1].Value,-20} {cells[2].Value,-12} {cells[3].Value}");
        }
        Console.WriteLine();
    }
}
