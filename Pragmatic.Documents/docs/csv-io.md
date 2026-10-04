# CSV Read and Write

The `Pragmatic.Documents.Csv` package reads and writes CSV files with RFC 4180 semantics plus locale variants. It shares the `SpreadsheetModel` with XLSX, so roundtrips are idempotent.

An optional source generator (`Pragmatic.Documents.Csv.Generator`) produces AOT-safe typed serialisers for your record types.

---

## API surface

### Writer

```csharp
public static class CsvWriter
{
    public static void Write(Stream stream, IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string?>> rows, CsvOptions? options = null);

    public static void Write(Stream stream, SpreadsheetModel model, CsvOptions? options = null);
    public static void Write(Stream stream, Sheet sheet, CsvOptions? options = null);

    public static byte[] WriteToArray(IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string?>> rows, CsvOptions? options = null);
}
```

### Reader

```csharp
public static class CsvReader
{
    public static SpreadsheetModel ReadAsModel(Stream stream, string sheetName = "Sheet1", CsvOptions? options = null);
    public static SpreadsheetModel ReadAsModel(byte[] data, string sheetName = "Sheet1", CsvOptions? options = null);
}
```

---

## Simple write

```csharp
using Pragmatic.Documents.Csv;

using var fs = File.Create("guests.csv");
CsvWriter.Write(fs,
    headers: ["Id", "First", "Last"],
    rows: new[]
    {
        new[] { "1", "Ada", "Lovelace" },
        new[] { "2", "Grace", "Hopper" },
    });
```

Output (RFC 4180, CRLF line endings, UTF-8 no BOM by default):

```
Id,First,Last
1,Ada,Lovelace
2,Grace,Hopper
```

---

## Write from a SpreadsheetModel

```csharp
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Csv;

var book = new SpreadsheetBuilder()
    .Sheet("Guests", s => s
        .HeaderRow("Id", "First", "Last")
        .Row(1, "Ada", "Lovelace")
        .Row(2, "Grace", "Hopper"))
    .Build();

using var fs = File.Create("guests.csv");
CsvWriter.Write(fs, book);    // exports the first sheet
```

For multi-sheet workbooks, CSV can only carry one sheet: write each sheet to its own file, or use XLSX instead.

---

## Options

```csharp
var italian = new CsvOptions
{
    Delimiter = ';',             // Italian Excel default
    DecimalSeparator = ',',      // 1,5 instead of 1.5
    QuoteChar = '"',
    LineEnding = "\r\n",         // CRLF (default); "\n" for Unix
    Encoding = Encoding.UTF8,    // default
    AlwaysQuote = false,         // quote only when needed
    WriteByteOrderMark = false,  // UTF-8 no BOM (default)
};

CsvWriter.Write(fs, book, italian);
```

The reader accepts the same `CsvOptions` so you can round-trip locale-specific CSVs without surprises.

---

## Formula-injection hardening

CSV opened in Excel can execute formulas if a cell starts with `=`, `+`, `-`, `@`, or tab. This is a known attack vector.

Enable hardening to prefix unsafe leading characters with an apostrophe:

```csharp
var safe = new CsvOptions { HardenAgainstFormulaInjection = true };
CsvWriter.Write(fs, book, safe);
```

When enabled:
- `=SUM(A1)` is written as `'=SUM(A1)` (leading apostrophe forces text interpretation in Excel)
- Plain data and legitimate negative numbers are unaffected

Use this for any CSV that goes to an untrusted user.

---

## Reading CSV

```csharp
using var fs = File.OpenRead("guests.csv");
var book = CsvReader.ReadAsModel(fs, sheetName: "Guests");

var sheet = book.Sheets[0];
foreach (var row in sheet.Rows)
{
    var cells = row.Cells.Select(c => c.Value?.ToString());
    Console.WriteLine(string.Join(" | ", cells));
}
```

### Reading with a locale

```csharp
var options = new CsvOptions { Delimiter = ';', DecimalSeparator = ',' };
using var fs = File.OpenRead("it-export.csv");
var book = CsvReader.ReadAsModel(fs, options: options);
```

### What the reader handles

- RFC 4180 quoting (`"embedded ""quote"""`, embedded commas, embedded newlines)
- CRLF, LF, CR line endings
- UTF-8 with or without BOM
- Empty rows (skipped)
- Leading/trailing whitespace preserved inside quoted fields, stripped in unquoted fields (RFC behaviour)

What it doesn't do:
- Auto-detect delimiter: specify `CsvOptions.Delimiter` if it's not `,`
- Infer column types: everything comes back as `string` in `Cell.Value`. If you need typed values, convert after reading.

---

## Typed serialisation with the source generator

```bash
dotnet add package Pragmatic.Documents.Csv.Generator
```

```csharp
using Pragmatic.Documents.Csv;

[CsvSerializable]
public partial record Guest
{
    public int Id { get; init; }
    [CsvColumn("First name")] public string First { get; init; } = "";
    [CsvColumn("Last name")]  public string Last { get; init; } = "";
    [CsvColumn("Check-in", Format = "yyyy-MM-dd")] public DateTime CheckIn { get; init; }
}
```

The generator adds a nested static `Csv` class to the partial type:

```csharp
// Generated at compile time, zero reflection
public partial record Guest
{
    public static class Csv
    {
        public static readonly string[] Headers = ["Id", "First name", "Last name", "Check-in"];
        public static void Write(Stream stream, IEnumerable<Guest> items, CsvOptions? options = null);
        public static byte[] WriteToArray(IEnumerable<Guest> items, CsvOptions? options = null);
        public static List<Guest> Read(Stream stream, CsvOptions? options = null);
        public static List<Guest> Read(byte[] data, CsvOptions? options = null);
    }
}
```

Use it:

```csharp
var guests = new[]
{
    new Guest { Id = 1, First = "Ada",   Last = "Lovelace", CheckIn = new DateTime(2026, 4, 1) },
    new Guest { Id = 2, First = "Grace", Last = "Hopper",   CheckIn = new DateTime(2026, 4, 2) },
};

using (var fs = File.Create("guests.csv"))
    Guest.Csv.Write(fs, guests);

using (var fs = File.OpenRead("guests.csv"))
    foreach (var g in Guest.Csv.Read(fs))
        Console.WriteLine($"{g.Id}: {g.First} {g.Last}");
```

`[CsvColumn]` is optional: `Header` (the constructor argument), `Format`, `Order` (0-based; unordered
properties fill the remaining slots in declaration order) and `Ignore`.

Types that round-trip: `string`, `int`, `long`, `float`, `double`, `decimal`, `bool`, `DateTime`,
`DateTimeOffset`, `Guid`, `TimeSpan`, enums, and their nullable variants. Any other type (`DateOnly`
and `TimeOnly` included) is written with `ToString()` and left at its default on read, reported as
**PRAG1900** (Warning). Change the type or mark the property `[CsvColumn(Ignore = true)]`.

A computed property with no setter (`public decimal Total => Net + Tax;`) is a column of the file and
is not read back: it recomputes from the columns that are. No diagnostic, because nothing is lost.

---

## Performance

- Writing is single-pass, constant-memory (reads headers + rows, writes as it goes).
- Reading materialises the whole file in memory: `ReadAsModel` as a `SpreadsheetModel`, the generated `Read` as a `List<T>`.

---

## Limitations

- No multi-sheet support (CSV is inherently single-sheet).
- No cell styling, formulas, or column widths; use XLSX if those matter.
- No delimiter auto-detection.

---

## Related

- [xlsx-rendering.md](xlsx-rendering.md): same model, full-featured output
- [`Pragmatic.Documents.Csv.Samples`](../samples/Pragmatic.Documents.Csv.Samples/README.md): basic roundtrip, locale, formula injection, edge cases
