# XLSX Rendering

The `Pragmatic.Documents.Xlsx` package reads and writes Excel workbooks (`.xlsx`, Office Open XML) using the shared `SpreadsheetModel`. Pure managed implementation, AOT-compatible.

---

## API surface

```csharp
public static class XlsxRenderer
{
    public static byte[] Render(SpreadsheetModel model);
    public static void RenderTo(Stream stream, SpreadsheetModel model);
    public static Task RenderToStreamAsync(Stream stream, SpreadsheetModel model, CancellationToken ct = default);
}

public static class XlsxReader
{
    public static SpreadsheetModel Read(byte[] data);
    public static SpreadsheetModel Read(Stream stream);
}
```

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

**Value vs Formula.** If a `Cell` sets both `Formula` and `Value`, the **formula wins** — the cell becomes a
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

- `CellStyle.Currency` — currency number format
- `CellStyle.Numeric(pattern)` — custom number format
- `CellStyle.Date` — ISO date
- `CellStyle.Percent` — percentage
- `CellStyle.Header` — bold + centred

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

The reader returns the same `SpreadsheetModel` type you'd build manually — roundtrips are preserved for the features the model supports (formulas, merged cells, column widths, freeze panes).

**Limits of the reader**: complex Excel features that don't have model equivalents (pivot tables, conditional formatting, charts, macros) are dropped on read. If you need those features round-tripped, don't use this library.

---

## Large data sets

For very wide exports (millions of rows), use `RenderToStreamAsync` and build rows lazily rather than materialising the whole `SpreadsheetModel`:

```csharp
// Build the model incrementally, feeding from a DB cursor
var sheet = new Sheet { Name = "Data" };
sheet.Rows.Add(new Row { Cells = { new Cell { Value = "Id" }, new Cell { Value = "Value" } } });

await foreach (var entity in dbCursor)
    sheet.Rows.Add(new Row { Cells = { new Cell { Value = entity.Id }, new Cell { Value = entity.Value } } });

var model = new SpreadsheetModel { Sheets = { sheet } };
await XlsxRenderer.RenderToStreamAsync(fs, model, ct);
```

For truly streaming output (constant memory regardless of row count), the current renderer is not the right fit — it writes the OOXML package in one pass. File an issue if you need a streaming XLSX writer.

---

## Limitations

- No pivot tables, charts, or conditional formatting.
- No macros (`.xlsm`) — read/write `.xlsx` only.
- Formulas are pass-through: the renderer does not evaluate them. Use Excel or a formula engine if you need computed values at render time.
- Images in cells are not supported (they'd need a floating-image pass; currently out of scope).

---

## Related

- [csv-io.md](csv-io.md) — same model, CSV output
- [`Pragmatic.Documents.Xlsx.Samples`](../samples/Pragmatic.Documents.Xlsx.Samples/README.md) — runnable scenarios (basic, multiple sheets, formulas, styled)
