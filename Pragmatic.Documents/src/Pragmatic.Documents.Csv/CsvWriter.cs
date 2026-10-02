using System.Globalization;
using System.Text;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv;

/// <summary>
/// RFC 4180 compliant CSV writer with culture-aware formatting.
/// </summary>
public static class CsvWriter
{
    /// <summary>Write CSV to a stream from headers and rows of string arrays.</summary>
    public static void Write(Stream stream, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string?>> rows, CsvOptions? options = null)
    {
        options ??= CsvOptions.Default;
        using var writer = new StreamWriter(stream, options.Encoding, leaveOpen: true);
        writer.NewLine = options.NewLine;

        if (options.HasHeaders)
            WriteRow(writer, headers, options.Delimiter, false); // Headers are trusted

        foreach (var row in rows)
            WriteRow(writer, row, options.Delimiter, options.FormulaProtection);
    }

    /// <summary>Write CSV to a stream from a SpreadsheetModel (first sheet).</summary>
    public static void Write(Stream stream, SpreadsheetModel model, CsvOptions? options = null)
    {
        if (model.Sheets.Count == 0) return;
        Write(stream, model.Sheets[0], options);
    }

    /// <summary>Write CSV to a stream from a single Sheet.</summary>
    public static void Write(Stream stream, Sheet sheet, CsvOptions? options = null)
    {
        options ??= CsvOptions.Default;
        using var writer = new StreamWriter(stream, options.Encoding, leaveOpen: true);
        writer.NewLine = options.NewLine;

        foreach (var row in sheet.Rows)
        {
            var values = row.Cells.Select(c => FormatValue(c.Value, options)).ToList();
            WriteRow(writer, values, options.Delimiter, options.FormulaProtection);
        }
    }

    /// <summary>Write CSV to a byte array.</summary>
    public static byte[] WriteToArray(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string?>> rows, CsvOptions? options = null)
    {
        using var ms = new MemoryStream();
        Write(ms, headers, rows, options);
        return ms.ToArray();
    }

    /// <summary>Write CSV to a byte array from a SpreadsheetModel.</summary>
    public static byte[] WriteToArray(SpreadsheetModel model, CsvOptions? options = null)
    {
        using var ms = new MemoryStream();
        Write(ms, model, options);
        return ms.ToArray();
    }

    internal static string FormatValue(object? value, CsvOptions options) => value switch
    {
        null => "",
        string s => s,
        DateTime dt => dt.ToString(options.DateFormat, options.Culture),
        DateTimeOffset dto => dto.ToString(options.DateFormat, options.Culture),
        decimal d => d.ToString(options.Culture),
        double d => d.ToString(options.Culture),
        float f => f.ToString(options.Culture),
        int i => i.ToString(options.Culture),
        long l => l.ToString(options.Culture),
        bool b => b ? "true" : "false",
        IFormattable fmt => fmt.ToString(null, options.Culture),
        _ => value.ToString() ?? ""
    };

    private static void WriteRow(StreamWriter writer, IReadOnlyList<string?> values, char delimiter, bool formulaProtection)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) writer.Write(delimiter);
            writer.Write(EscapeField(values[i] ?? "", delimiter, formulaProtection));
        }
        writer.WriteLine();
    }

    /// <summary>
    /// RFC 4180: fields containing delimiter, quote, or newline must be enclosed in double quotes.
    /// Double quotes within are escaped as "".
    /// When formulaProtection is enabled, prefixes dangerous characters (=, +, -, @) with a tab
    /// to prevent formula injection in Excel/LibreOffice.
    /// </summary>
    internal static string EscapeField(string value, char delimiter, bool formulaProtection = true)
    {
        if (value.Length == 0) return value;

        // Formula-injection protection: prepend a tab to the field CONTENT (not as out-of-quote
        // padding) so Excel/LibreOffice see a leading non-formula character. By making the guard
        // part of the value before RFC quoting, the field still round-trips: an RFC-4180 parser
        // strips the surrounding quotes and yields "\t=...", which spreadsheets treat as text.
        // OWASP CSV-injection lead-in set: = + - @ plus a leading tab or carriage-return (which some
        // clients strip, re-exposing a following formula character).
        var content = formulaProtection && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "\t" + value
            : value;

        // RFC 4180: a field must be quoted if it contains the delimiter, a quote, CR/LF, or a
        // leading tab guard (tab is whitespace many parsers would otherwise trim). Inside quotes,
        // each '"' is doubled.
        var needsQuoting = content.Contains(delimiter)
                        || content.Contains('"')
                        || content.Contains('\n')
                        || content.Contains('\r')
                        || content.Contains('\t');

        return needsQuoting ? $"\"{content.Replace("\"", "\"\"")}\"" : content;
    }
}
