using System.Text;
using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv.Samples.Samples;

/// <summary>
///     CSVs opened in Excel / LibreOffice / Numbers execute cells starting with
///     `=`, `+`, `-`, or `@` as formulas. When the data comes from untrusted
///     input this is a remote-code-execution vector. The writer protects against
///     it by default — prefixing those characters with a tab — and the reader
///     parses them back unchanged.
/// </summary>
public static class FormulaInjectionSample
{
    public static void Run()
    {
        Console.WriteLine("--- Formula injection protection ---");

        string[] headers = ["Name", "Comment"];
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Alice", "=1+1" },                     // classic injection attempt
            new string?[] { "Bob",   "=cmd|' /C calc'!A0" },       // real-world DDE exploit shape
            new string?[] { "Carol", "@SUM(A1:A10)" },             // leading @ triggers formula too
            new string?[] { "Dave",  "+1234567890" },              // phone numbers look innocent but trigger
            new string?[] { "Eve",   "-100" },                     // negative numbers do too
            new string?[] { "Frank", "Normal comment, no issue" }, // untouched
        };

        // Protected (default).
        var protectedBytes = CsvWriter.WriteToArray(headers, rows);
        Console.WriteLine("With FormulaProtection = true (default):");
        Console.WriteLine(Encoding.UTF8.GetString(protectedBytes));

        // Opt out when you trust the data and need the raw characters to stay.
        var unprotected = CsvWriter.WriteToArray(
            headers,
            rows,
            CsvOptions.Default with { FormulaProtection = false });
        Console.WriteLine("With FormulaProtection = false (opt-in, data is trusted):");
        Console.WriteLine(Encoding.UTF8.GetString(unprotected));

        Console.WriteLine();
    }
}
