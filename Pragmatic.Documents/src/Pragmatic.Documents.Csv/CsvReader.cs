using System.Globalization;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv;

/// <summary>
/// RFC 4180 compliant CSV reader with culture-aware parsing.
/// </summary>
public static class CsvReader
{
    /// <summary>Read CSV from a stream, returning headers and rows of string arrays.</summary>
    /// <remarks>
    ///     Materializes every row — bounded by <see cref="CsvOptions.MaxRows"/> /
    ///     <see cref="CsvOptions.MaxFieldChars"/>. For large files that don't need to be held in
    ///     memory at once, use the streaming <see cref="ReadRows"/> instead.
    /// </remarks>
    public static (string[] Headers, List<string[]> Rows) Read(Stream stream, CsvOptions? options = null)
    {
        options ??= CsvOptions.Default;

        var allRows = ReadRows(stream, options).ToList();

        if (options.HasHeaders && allRows.Count > 0)
        {
            var headers = allRows[0];
            var rows = allRows.GetRange(1, allRows.Count - 1);
            return (headers, rows);
        }

        return ([], allRows);
    }

    /// <summary>
    ///     Streams CSV rows one at a time (constant memory per row, including the header row when
    ///     present). The stream is read lazily during enumeration and left open.
    /// </summary>
    public static IEnumerable<string[]> ReadRows(Stream stream, CsvOptions? options = null)
    {
        options ??= CsvOptions.Default;
        using var reader = new StreamReader(stream, options.Encoding, leaveOpen: true);
        foreach (var row in EnumerateRows(reader, options))
            yield return row;
    }

    /// <summary>Read CSV from a byte array.</summary>
    public static (string[] Headers, List<string[]> Rows) Read(byte[] data, CsvOptions? options = null)
    {
        using var ms = new MemoryStream(data);
        return Read(ms, options);
    }

    /// <summary>Read CSV into a SpreadsheetModel with a single sheet.</summary>
    public static SpreadsheetModel ReadAsModel(Stream stream, string sheetName = "Sheet1", CsvOptions? options = null)
    {
        var (headers, rows) = Read(stream, options);
        return ToModel(headers, rows, sheetName, options);
    }

    /// <summary>Read CSV from bytes into a SpreadsheetModel.</summary>
    public static SpreadsheetModel ReadAsModel(byte[] data, string sheetName = "Sheet1", CsvOptions? options = null)
    {
        using var ms = new MemoryStream(data);
        return ReadAsModel(ms, sheetName, options);
    }

    private static SpreadsheetModel ToModel(string[] headers, List<string[]> rows, string sheetName, CsvOptions? options)
    {
        options ??= CsvOptions.Default;
        var headerStyle = new CellStyle { Bold = true };

        var modelRows = new List<Row>();

        if (headers.Length > 0)
        {
            var headerCells = headers.Select(h => new Cell { Value = h, Style = headerStyle }).ToList();
            modelRows.Add(new Row(headerCells));
        }

        foreach (var row in rows)
        {
            var cells = row.Select(v => new Cell { Value = ParseValue(v, options) }).ToList();
            modelRows.Add(new Row(cells));
        }

        var sheet = new Sheet { Name = sheetName, Rows = modelRows };
        return new SpreadsheetModel { Sheets = [sheet] };
    }

    /// <summary>Try to parse a string value into a typed value (number, date, bool).</summary>
    internal static object? ParseValue(string? value, CsvOptions options)
    {
        if (string.IsNullOrEmpty(value)) return null;

        // Boolean
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        // Integer
        if (long.TryParse(value, NumberStyles.Integer, options.Culture, out var longVal))
        {
            if (longVal is >= int.MinValue and <= int.MaxValue)
                return (int)longVal;
            return longVal;
        }

        // Decimal/double
        if (decimal.TryParse(value, NumberStyles.Number, options.Culture, out var decVal))
            return decVal;

        // Date
        if (DateTime.TryParseExact(value, options.DateFormat, options.Culture, DateTimeStyles.None, out var dateVal))
            return dateVal;

        return value;
    }

    private static IEnumerable<string[]> EnumerateRows(StreamReader reader, CsvOptions options)
    {
        var delimiter = options.Delimiter;
        var rowCount = 0;
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        while (true)
        {
            var ch = reader.Read();
            if (ch == -1)
            {
                if (inQuotes)
                    throw new FormatException("CSV parse error: unclosed quoted field at end of input.");

                // End of stream — flush any pending field
                if (field.Length > 0 || fields.Count > 0)
                {
                    fields.Add(field.ToString());
                    GuardRowCount(++rowCount, options);
                    yield return fields.ToArray();
                }
                break;
            }

            var c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Peek next char
                    var next = reader.Peek();
                    if (next == '"')
                    {
                        // Escaped quote
                        reader.Read();
                        AppendGuarded(field, '"', options);
                    }
                    else
                    {
                        // End of quoted field
                        inQuotes = false;
                    }
                }
                else
                {
                    AppendGuarded(field, c, options);
                }
            }
            else
            {
                if (c == '"' && field.Length == 0)
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    // Check for \r\n
                    if (reader.Peek() == '\n') reader.Read();
                    fields.Add(field.ToString());
                    GuardRowCount(++rowCount, options);
                    yield return fields.ToArray();
                    fields = [];
                    field.Clear();
                }
                else if (c == '\n')
                {
                    fields.Add(field.ToString());
                    GuardRowCount(++rowCount, options);
                    yield return fields.ToArray();
                    fields = [];
                    field.Clear();
                }
                else
                {
                    AppendGuarded(field, c, options);
                }
            }
        }
    }

    private static void AppendGuarded(System.Text.StringBuilder field, char c, CsvOptions options)
    {
        if (field.Length >= options.MaxFieldChars)
            throw new FormatException(
                $"CSV field exceeds the {options.MaxFieldChars:N0}-character limit (CsvOptions.MaxFieldChars).");
        field.Append(c);
    }

    private static void GuardRowCount(int rowCount, CsvOptions options)
    {
        if (rowCount > options.MaxRows)
            throw new FormatException(
                $"CSV input exceeds the {options.MaxRows:N0}-row limit (CsvOptions.MaxRows).");
    }
}
