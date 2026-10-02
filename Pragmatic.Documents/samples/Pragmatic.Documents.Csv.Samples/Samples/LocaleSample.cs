using System.Text;
using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv.Samples.Samples;

/// <summary>
///     European spreadsheets ship with semicolon delimiters and comma decimals.
///     `CsvOptions.Italian` is a ready-made preset; the same options drive both
///     writer and reader so the round-trip is symmetric.
/// </summary>
public static class LocaleSample
{
    public static void Run()
    {
        Console.WriteLine("--- Italian locale (semicolon delimiter, dd/MM/yyyy) ---");

        string[] headers = ["Codice", "Descrizione", "Importo", "Data"];
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "A-001", "Consulenza",            "1.234,56", "14/03/2026" },
            new string?[] { "A-002", "Noleggio attrezzatura", "890,00",   "27/03/2026" },
        };

        var bytes = CsvWriter.WriteToArray(headers, rows, CsvOptions.Italian);
        Console.WriteLine("Produced CSV (Italian preset):");
        Console.WriteLine(Encoding.UTF8.GetString(bytes));

        var (parsedHeaders, parsedRows) = CsvReader.Read(bytes, CsvOptions.Italian);
        Console.WriteLine($"Parsed back: headers = [{string.Join(", ", parsedHeaders)}]");
        foreach (var row in parsedRows)
            Console.WriteLine($"  {string.Join(" | ", row)}");

        Console.WriteLine();
    }
}
