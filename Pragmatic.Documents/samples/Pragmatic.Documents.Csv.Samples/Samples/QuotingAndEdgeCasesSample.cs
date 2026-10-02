using System.Text;
using Pragmatic.Documents.Csv;

namespace Pragmatic.Documents.Csv.Samples.Samples;

/// <summary>
///     The writer follows RFC 4180 quoting rules: fields that contain the
///     delimiter, a newline, or a double quote are wrapped in quotes, and
///     embedded quotes are doubled. The reader is the inverse. This sample
///     exercises the common edge cases so you can see them round-trip.
/// </summary>
public static class QuotingAndEdgeCasesSample
{
    public static void Run()
    {
        Console.WriteLine("--- Quoting & edge cases ---");

        string[] headers = ["Category", "Value"];
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "plain",          "simple" },
            new string?[] { "comma-inside",   "one, two, three" },          // contains delimiter
            new string?[] { "newline-inside", "line 1\nline 2" },            // contains LF
            new string?[] { "crlf-inside",    "row 1\r\nrow 2" },            // contains CRLF
            new string?[] { "quote-inside",   "she said \"hi\"" },           // contains double quote
            new string?[] { "all-together",   "comma, \"and\"\nnewline" },   // everything at once
            new string?[] { "empty",          "" },                          // empty cell
            new string?[] { "null",           null },                        // null becomes empty
            new string?[] { "leading-space",  "  padded" },                  // preserved as-is
        };

        var bytes = CsvWriter.WriteToArray(headers, rows);
        Console.WriteLine("Produced CSV:");
        Console.WriteLine(Encoding.UTF8.GetString(bytes));

        var (_, parsedRows) = CsvReader.Read(bytes);
        Console.WriteLine($"Parsed back {parsedRows.Count} rows:");
        for (var i = 0; i < parsedRows.Count; i++)
            Console.WriteLine($"  [{i}] {string.Join(" | ", parsedRows[i].Select(f => $"\"{f}\""))}");

        Console.WriteLine();
    }
}
