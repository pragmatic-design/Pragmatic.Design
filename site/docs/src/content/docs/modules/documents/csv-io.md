---
title: "CSV Read and Write"
description: "The `Pragmatic.Documents.Csv` package reads and writes CSV files with RFC 4180 semantics plus locale variants. It shares the `SpreadsheetModel` with XLSX, so ro"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/csv-io.md
sidebar:
  order: 3
---
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
    public static (string[] Headers, List<string[]> Rows) Read(Stream stream, CsvOptions? options = null);
    public static (string[] Headers, List<string[]> Rows) Read(byte[] data, CsvOptions? options = null);
    public static IEnumerable<string[]> ReadRows(Stream stream, CsvOptions? options = null); // streams, one row at a time

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
    Delimiter = ';',                                // Italian Excel default
    Culture = CultureInfo.GetCultureInfo("it-IT"),  // 1,5 instead of 1.5
    DateFormat = "dd/MM/yyyy",
    NewLine = "\r\n",                               // default: Environment.NewLine
    Encoding = new UTF8Encoding(false),             // default: UTF-8 without BOM
    HasHeaders = true,                              // default
    FormulaProtection = true,                       // default, see above
};
// The same three as CsvOptions.Italian. MaxRows, MaxFieldsPerRow and MaxFieldChars bound what the reader accepts.

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
// Semicolon delimiter, it-IT numbers (1.234,56) and dd/MM/yyyy dates.
using var fs = File.OpenRead("it-export.csv");
var book = CsvReader.ReadAsModel(fs, options: CsvOptions.Italian);
```

### What the reader handles

- RFC 4180 quoting (`"embedded ""quote"""`, embedded delimiters, embedded newlines)
- CRLF, LF and CR line endings, mixed in one file
- UTF-8 with or without BOM; bytes that are not UTF-8 are read as U+FFFD, not refused
- Whitespace kept as it is, quoted or not
- A quote inside an unquoted field (`a"b`) is kept as a character
- An empty line is a row with one empty field

`ReadAsModel` types each cell it can: `true`/`false`, an integer (`int`, or `long` when it does not fit), a
`decimal`, a date in `CsvOptions.DateFormat`, and the text otherwise, all in `CsvOptions.Culture`. `Read` and
`ReadRows` return the fields as written.

What it doesn't do:
- Auto-detect the delimiter: set `CsvOptions.Delimiter` if it's not `,`

### A file you did not write

An uploaded file is read or refused, and the refusal is a `FormatException` whose message starts with `CSV`:

- a quoted field still open at the end of the input;
- a field longer than `CsvOptions.MaxFieldChars` (1,000,000 characters by default);
- a row with more fields than `CsvOptions.MaxFieldsPerRow` (16,384 by default, Excel's column limit);
- more rows than `CsvOptions.MaxRows` (1,000,000 by default).

The three hold for the streaming `ReadRows` as well. The fields limit is what bounds a line of delimiters, which is
one row of as many empty fields as it has characters under the other two: one megabyte of commas allocated 40 MB
before it existed. The reader is fuzzed (`node scripts/fuzz.mjs csv-reader`) and has property tests for these
outcomes; anything else it throws is a defect.

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

- [xlsx-rendering.md](/modules/documents/xlsx-rendering/): same model, full-featured output
- [`Pragmatic.Documents.Csv.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Csv.Samples/README.md): basic roundtrip, locale, formula injection, edge cases
