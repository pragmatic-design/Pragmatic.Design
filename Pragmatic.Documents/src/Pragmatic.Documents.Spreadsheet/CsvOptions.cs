using System.Globalization;
using System.Text;

namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Options for CSV reading and writing, including culture-specific formatting.
/// </summary>
public sealed record CsvOptions
{
    /// <summary>Field delimiter character. Default: ','.</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Culture for number/date parsing and formatting. Default: InvariantCulture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>Date format string for DateTime values. Default: "yyyy-MM-dd".</summary>
    public string DateFormat { get; init; } = "yyyy-MM-dd";

    /// <summary>Text encoding. Default: UTF-8 without BOM.</summary>
    public Encoding Encoding { get; init; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Whether the first row contains column headers. Default: true.</summary>
    public bool HasHeaders { get; init; } = true;

    /// <summary>Line separator for writing. Default: Environment.NewLine.</summary>
    public string NewLine { get; init; } = Environment.NewLine;

    /// <summary>
    /// Protect against CSV formula injection by prefixing dangerous characters (=, +, -, @) with a tab.
    /// Enabled by default to prevent untrusted data from executing formulas in Excel/LibreOffice.
    /// </summary>
    public bool FormulaProtection { get; init; } = true;

    /// <summary>
    /// Maximum number of rows accepted when reading. Guards against unbounded memory growth on
    /// untrusted/huge inputs (the reader materializes rows). Default: 1,000,000.
    /// Set <see cref="int.MaxValue"/> to disable. The streaming API (<c>CsvReader.ReadRows</c>)
    /// enforces it too — raise it there when intentionally scanning larger files.
    /// </summary>
    public int MaxRows { get; init; } = 1_000_000;

    /// <summary>
    /// Maximum characters accepted in a single field when reading. Guards against a single
    /// unterminated/huge field buffering the whole input. Default: 1,000,000.
    /// </summary>
    public int MaxFieldChars { get; init; } = 1_000_000;

    /// <summary>Default options with comma delimiter and invariant culture.</summary>
    public static CsvOptions Default { get; } = new();

    /// <summary>Italian-style options: semicolon delimiter, it-IT culture, dd/MM/yyyy dates.</summary>
    public static CsvOptions Italian { get; } = new()
    {
        Delimiter = ';',
        Culture = CultureInfo.GetCultureInfo("it-IT"),
        DateFormat = "dd/MM/yyyy"
    };
}
