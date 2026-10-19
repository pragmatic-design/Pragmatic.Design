---
title: "XLSX Rendering"
description: "The `Pragmatic.Documents.Xlsx` package reads and writes Excel workbooks (`.xlsx`, Office Open XML) using the shared `SpreadsheetModel`. Pure managed implementat"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/xlsx-rendering.md
sidebar:
  order: 9
---
The `Pragmatic.Documents.Xlsx` package reads and writes Excel workbooks (`.xlsx`, Office Open XML) using the shared `SpreadsheetModel`. Pure managed implementation, AOT-compatible.

---

## API surface

```csharp
public static class XlsxRenderer
{
    public static byte[] Render(SpreadsheetModel model);
    public static void RenderTo(Stream stream, SpreadsheetModel model);
}

public static class XlsxReader
{
    public static SpreadsheetModel Read(byte[] data);
    public static SpreadsheetModel Read(Stream stream);
}
```

There are no `…Async` methods. Writing and reading a workbook is CPU work: on a server, call them on
the request's own thread. Wrapping them in `Task.Run` frees no thread, it only adds a hop.

---

## Basic example

```csharp
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx;

var book = new SpreadsheetBuilder()
    .Title("Sales Export")
    .Sheet("Q1", s => s
        .HeaderRow("Month", "Region", "Revenue")
        .Row("Jan", "EU", 120_000)
        .Row("Feb", "EU", 135_500)
        .Row("Mar", "EU", 150_250))
    .Build();

using var fs = File.Create("sales.xlsx");
XlsxRenderer.RenderTo(fs, book);
```

---

## Builder API

### Sheet configuration

```csharp
.Sheet("Sales", s => s
    .Column(width: 12)
    .Column(width: 10)
    .Column(width: 16, style: CellStyle.Currency)
    .Freeze(rows: 1, columns: 1)          // header row + first column always visible
    .Merge("A1", "C1")                     // merge cells across the banner
    .HeaderRow("Q1 Sales", "", "")
    .HeaderRow("Month", "Region", "Revenue")
    .Row("Jan", "EU", 120_000)
    // ...
)
```

### Rows

| Method | Semantics |
|--------|-----------|
| `.HeaderRow(params string[])` | Applies header styling (bold, fill colour) |
| `.Row(params object?[])` | Plain data row; types auto-detected (number, string, bool, date) |
| `.FormulaRow(params string?[])` | Each non-empty cell is a formula (`=SUM(A2:A10)`, `=A2*B2`) |
| `.StyledRow(params Cell[])` | Full control over per-cell styling |
| `.Row(Row row)` | Append a pre-built `Row` object |

### Cells

For per-cell control:

```csharp
.StyledRow(
    new Cell { Value = "VIP",   Style = new CellStyle { Bold = true, FillColor = "#FFECB3" } },
    new Cell { Value = 42,      Style = CellStyle.Numeric("#,##0") },
    new Cell { Formula = "=B2*1.22", Style = CellStyle.Currency })
```

**Value vs Formula.** If a `Cell` sets both `Formula` and `Value`, the **formula wins**: the cell becomes a
formula cell and `Value` is written as its cached result (what a viewer shows until it recalculates). A
`DateTime`/`DateTimeOffset` value with no explicit `CellStyle.NumberFormat` is given a default date format
automatically, so it shows as a date rather than a raw serial number. A value of an unsupported CLR type
(anything other than `string`, `double`, `decimal`, `DateTime`, `bool`) is written as its text form.

---

## Multi-sheet workbooks

```csharp
var book = new SpreadsheetBuilder()
    .Title("Quarterly reports")
    .Sheet("Q1", s => s.HeaderRow("Month", "Revenue").Row("Jan", 120_000))
    .Sheet("Q2", s => s.HeaderRow("Month", "Revenue").Row("Apr", 180_000))
    .Sheet("Summary", s => s
        .HeaderRow("Quarter", "Revenue")
        .FormulaRow("Q1", "=SUM(Q1!B:B)")
        .FormulaRow("Q2", "=SUM(Q2!B:B)"))
    .Build();
```

Cross-sheet formulas (`=SUM(Q1!B:B)`) are preserved verbatim.

---

## Styling

`CellStyle` controls font, fill, borders, and number formats:

```csharp
var currency = new CellStyle
{
    NumberFormat = "€#,##0.00",
    Bold = false,
    FontColor = "#000000",
    Alignment = CellAlignment.Right,
};

var header = new CellStyle
{
    Bold = true,
    FillColor = "#1F497D",
    FontColor = "#FFFFFF",
    Alignment = CellAlignment.Center,
};
```

Pre-defined styles:

- `CellStyle.Currency`: currency number format
- `CellStyle.Numeric(pattern)`: custom number format
- `CellStyle.Date`: ISO date
- `CellStyle.Percent`: percentage
- `CellStyle.Header`: bold + centred

---

## Freeze panes and merges

```csharp
.Freeze(rows: 2, columns: 1)      // two top rows + first column always visible
.FreezeRows(1)                     // shortcut for header row
.Merge("A1", "D1")                 // merge A1:D1 into a single cell
```

Coordinates use A1 notation (`A1`, `B2`, `AA27`).

---

## Reading XLSX

```csharp
using var fs = File.OpenRead("imported.xlsx");
var book = XlsxReader.Read(fs);

foreach (var sheet in book.Sheets)
{
    Console.WriteLine($"Sheet: {sheet.Name} ({sheet.Rows.Count} rows)");
    foreach (var row in sheet.Rows)
        Console.WriteLine(string.Join(" | ", row.Cells.Select(c => c.Value)));
}
```

The reader returns the same `SpreadsheetModel` type you'd build manually; roundtrips are preserved for the features the model supports (formulas, merged cells, column widths, freeze panes).

**Limits of the reader**: complex Excel features that don't have model equivalents (pivot tables, conditional formatting, charts, macros) are dropped on read. If you need those features round-tripped, don't use this library.

### A file you did not write

An uploaded file is read or refused, and the refusal is one of three exceptions:

| Exception | When |
|---|---|
| `InvalidDataException` | The file is not a ZIP, or the archive is damaged |
| `XmlException` | A part is not well-formed XML, or declares a DTD |
| `InvalidOperationException`, message starting `XLSX` | A limit is exceeded (256 sheets, 1,000,000 shared strings, 5,000,000 cells in a sheet, 100 MB in a part), a sheet points outside `xl/worksheets/`, or the workbook names a sheet the package does not hold |

A sheet the workbook declares and the package does not hold is refused, not read as an empty sheet: an import
would otherwise succeed with no rows where the file had data. The reader is fuzzed (`node scripts/fuzz.mjs
xlsx-reader`) and has property tests for these outcomes; anything else it throws is a defect.

---

## Large data sets

`RenderTo` writes the package into the destination stream, but the `SpreadsheetModel` holds every row
until the render ends, so memory grows with the rows. The figures below come from the Documents
benchmarks' `memory` mode (`-- memory`), a ten-column sheet built with `SpreadsheetBuilder` and written
to a `MemoryStream`, on Windows, .NET 10, on 2026-10-16, each size in a process of its own:

| Rows | XLSX bytes | Managed heap peak | Process peak working set |
|---:|---:|---:|---:|
| 1,000 | 88,056 | 3.6 MB | 40 MB |
| 10,000 | 892,731 | 19 MB | 66 MB |
| 50,000 | 4,409,079 | 81 MB | 138 MB |

About 1.5 KB of managed heap per row, the model and the output buffer together; writing to a file
stream instead of a `MemoryStream` takes the output out of that figure. Feeding the model from a
cursor saves the source collection, not the model:

```csharp
// Build the model from a DB cursor
var sheet = new Sheet { Name = "Data" };
sheet.Rows.Add(new Row { Cells = { new Cell { Value = "Id" }, new Cell { Value = "Value" } } });

await foreach (var entity in dbCursor)
    sheet.Rows.Add(new Row { Cells = { new Cell { Value = entity.Id }, new Cell { Value = entity.Value } } });

var model = new SpreadsheetModel { Sheets = { sheet } };
XlsxRenderer.RenderTo(fs, model);
```

For truly streaming output (constant memory regardless of row count), the current renderer is not the right fit: it writes the OOXML package in one pass. File an issue if you need a streaming XLSX writer.

---

## Limitations

- No pivot tables, charts, or conditional formatting.
- No macros (`.xlsm`): read/write `.xlsx` only.
- Formulas are pass-through: the renderer does not evaluate them. Use Excel or a formula engine if you need computed values at render time.
- Images in cells are not supported (they'd need a floating-image pass; currently out of scope).

---

## Related

- [csv-io.md](/modules/documents/csv-io/): same model, CSV output
- [`Pragmatic.Documents.Xlsx.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Xlsx.Samples/README.md): runnable scenarios (basic, multiple sheets, formulas, styled)
