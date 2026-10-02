using System.Text;
using Pragmatic.Documents.Csv;

namespace Pragmatic.Documents.Csv.Samples.Samples;

/// <summary>
///     Smallest possible happy path: write headers + rows to a CSV byte array,
///     read them back, verify the shape. This is the "hello world" for the module.
/// </summary>
public static class BasicRoundTripSample
{
    public static void Run()
    {
        Console.WriteLine("--- Basic write + read round-trip ---");

        string[] headers = ["Id", "Name", "Email", "SignupDate"];
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "1", "Alice", "alice@example.com", "2026-01-14" },
            new string?[] { "2", "Bob",   "bob@example.com",   "2026-02-03" },
            new string?[] { "3", "Carol", "carol@example.com", "2026-03-27" },
        };

        var bytes = CsvWriter.WriteToArray(headers, rows);

        Console.WriteLine("Produced CSV:");
        Console.WriteLine(Encoding.UTF8.GetString(bytes));

        var (parsedHeaders, parsedRows) = CsvReader.Read(bytes);
        Console.WriteLine(
            $"Parsed back: {parsedHeaders.Length} headers, {parsedRows.Count} rows");
        foreach (var row in parsedRows)
            Console.WriteLine($"  {string.Join(" | ", row)}");

        Console.WriteLine();
    }
}
