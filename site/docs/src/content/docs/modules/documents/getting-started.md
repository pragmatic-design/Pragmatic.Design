---
title: "Getting Started"
description: "Five concrete scenarios, each ~5 minutes. Pick the one that matches your immediate need."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/getting-started.md
sidebar:
  order: 2
---
Five concrete scenarios, each ~5 minutes. Pick the one that matches your immediate need.

⚠️ The scenarios build models in C#, which is right when code decides the layout. A document or a mail
whose **wording** somebody will change (a letter, an invoice, a notification) is a template: start from
[Templates and markup](#templates-and-markup-the-default-for-documents-and-mail).

---

## Scenario 1: PDF from C#

Produce an invoice PDF with a heading, a paragraph, and a simple table.

### Install

```bash
dotnet add package Pragmatic.Documents.Model
dotnet add package Pragmatic.Documents.Pdf
```

### Code

```csharp
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

var doc = new DocumentBuilder()
    .Title("Invoice 2026-001")
    .Author("Acme Corp")
    .Size(PageSize.A4)
    .WithMargins(Margins.Default)
    .Page(p => p
        .Heading("Invoice 2026-001", level: 1)
        .Paragraph(new TextNode { Content = "Thank you for your business." })
        .Spacer(10)
        .Table(t => t
            .HeaderRow("Item", "Qty", "Price")
            .Row("Design review", "1", "€250.00")
            .Row("Implementation", "3", "€900.00"))
        .Spacer(20)
        .Paragraph(new TextNode { Content = "Total: €1150.00", Style = new NodeStyle { FontWeight = FontWeight.Bold } }))
    .Build();

using var fs = File.Create("invoice.pdf");
PdfRenderer.RenderTo(fs, doc);
```

### What happens

- `DocumentBuilder` constructs an immutable `DocumentModel` with document metadata and a single page
- `PdfRenderer.RenderTo` writes the PDF directly into the stream, with no intermediate byte array

Open `invoice.pdf` in any viewer. See [pdf-rendering.md](/modules/documents/pdf-rendering/) for fonts, images, TOCs, and page numbering.

---

## Scenario 2: Same content, PDF + DOCX

The document model is renderer-agnostic. Build once, render to both.

```bash
dotnet add package Pragmatic.Documents.Docx
```

```csharp
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Docx;

DocumentModel doc = BuildInvoice();   // same builder as before

using (var pdf = File.Create("invoice.pdf"))
    PdfRenderer.RenderTo(pdf, doc);

using (var docx = File.Create("invoice.docx"))
    DocxRenderer.RenderTo(docx, doc);
```

Features like `Toc(...)` and page numbering `Field`s render correctly in both formats (viewers that auto-update fields refresh them on open). See [docx-rendering.md](/modules/documents/docx-rendering/) for DOCX-specific options.

---

## Scenario 3: XLSX from tabular data

Export a list of guests to a styled XLSX.

```bash
dotnet add package Pragmatic.Documents.Spreadsheet
dotnet add package Pragmatic.Documents.Xlsx
```

```csharp
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx;

var book = new SpreadsheetBuilder()
    .Title("Guest Export")
    .Author("Front Desk")
    .Sheet("Guests", s => s
        .Column(width: 8)          // ID column
        .Column(width: 20)         // First name
        .Column(width: 20)         // Last name
        .Column(width: 28)         // Email
        .Freeze(rows: 1, columns: 0)   // header row always visible
        .HeaderRow("Id", "First", "Last", "Email")
        .Row(1, "Ada", "Lovelace", "ada@example.com")
        .Row(2, "Grace", "Hopper", "grace@example.com")
        .FormulaRow("=COUNTA(A2:A3)", "", "", ""))     // count of rows
    .Build();

using var fs = File.Create("guests.xlsx");
XlsxRenderer.RenderTo(fs, book);
```

The freeze pane and formula work when opened in Excel or LibreOffice. See [xlsx-rendering.md](/modules/documents/xlsx-rendering/) for multi-sheet, merged cells, and per-cell styling.

---

## Scenario 4: CSV read + write

Read an existing CSV, add a column, write it back.

```bash
dotnet add package Pragmatic.Documents.Csv
```

```csharp
using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;

using var input = File.OpenRead("guests.csv");
var book = CsvReader.ReadAsModel(input, sheetName: "Guests");

// Add a "FullName" column
var sheet = book.Sheets[0];
sheet.Rows[0].Cells.Add(new Cell { Value = "FullName" });
for (int i = 1; i < sheet.Rows.Count; i++)
{
    var first = sheet.Rows[i].Cells[1].Value;
    var last  = sheet.Rows[i].Cells[2].Value;
    sheet.Rows[i].Cells.Add(new Cell { Value = $"{first} {last}" });
}

using var output = File.Create("guests-enriched.csv");
CsvWriter.Write(output, book);
```

CSV handles quoting, CRLF, embedded commas, and RFC 4180 edge cases out of the box. For locale-aware variants (`;` separator, `,` decimal) and formula-injection hardening see [csv-io.md](/modules/documents/csv-io/).

---

## Scenario 5: HTML email

Build a verification email with a hero section and a call-to-action.

```bash
dotnet add package Pragmatic.Email.Model
dotnet add package Pragmatic.Documents.Email
```

```csharp
using Pragmatic.Email.Model;
using Pragmatic.Documents.Email;

var email = new EmailBuilder()
    .Subject("Verify your email address")
    .Preheader("One click to finish signing up.")
    .Language("en")
    .Width(600)
    .BackgroundColor("#f4f5f7")
    .Section(s => s
        .Heading("Welcome, Alice!")
        .Paragraph("Thanks for signing up for Acme. We need to confirm your email before you can get started.")
        .Button("Verify your email", "https://app.acme.com/verify?t=abc123")
        .Paragraph("If you didn't create an account, you can safely ignore this email."))
    .Build();

string html = new EmailHtmlRenderer().Render(email);
File.WriteAllText("verify.html", html);

// Hand the HTML to Pragmatic.Email or your SMTP library
```

The renderer emits email-client-safe HTML: inline CSS, MSO-compatible tables, preheader text, proper DOCTYPE. See [email-rendering.md](/modules/documents/email-rendering/) for advanced layouts (two-column, hero image, footer).

---

## Beyond the five scenarios

### Templates and markup: the default for documents and mail

A letter, an invoice, a notification mail, anything whose wording somebody will want to change, is a
template, not C#. The markup is XML ([markup-parser.md](/modules/documents/markup-parser/)):

```xml
<!-- templates/invoice.pdxdoc -->
<document title="Invoice {{ invoice.number }}">
  <page>
    <heading level="1">Invoice {{ invoice.number }}</heading>
    <text>Thank you for your business, {{ customer.name }}.</text>
    <table data-source="items">
      <column>Item</column>
      <column align="right">Qty</column>
      <column align="right">Price</column>
      <row-template>
        <cell>{{ item.name }}</cell>
        <cell>{{ item.qty }}</cell>
        <cell>{{ item.price | currency:"EUR" }}</cell>
      </row-template>
    </table>
    <text>Total: {{ invoice.total | currency:"EUR" }}</text>
  </page>
</document>
```

Ship it inside the module (`<EmbeddedResource Include="templates\*.pdxdoc;templates\*.pdxemail" />`),
declare it once, and compose it where it is wanted:

```csharp
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templating.Data;

[assembly: PdxTemplates<BillingModule>]   // module: the generated host registers the source

// IPdxTemplates templates, injected
var data = new TemplateDataContext()
    .AddSource("invoice", new Dictionary<string, object?> { ["number"] = "2026-001", ["total"] = 1150m })
    .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Alice" })
    .AddSource("items", new List<Dictionary<string, object?>>
    {
        new() { ["name"] = "Design review", ["qty"] = 1, ["price"] = 250m },
    });

var document = await templates.DocumentAsync("invoice.pdxdoc", "en-US", data, ct);

using var fs = File.Create("invoice.pdf");
PdfRenderer.RenderTo(fs, document.Model);
```

The second argument is the **reader's** language: it governs `{{ t:… }}` translations and the `date`,
`currency` and `percent` pipes. `EmailAsync` is the same call for a `.pdxemail`, and returns the subject,
the HTML and the plain text. See [templating.md](/modules/documents/templating/) for expressions and pipes.

### Typed CSV with AOT

For AOT apps where the generator emits CSV serialisation at compile time:

```bash
dotnet add package Pragmatic.Documents.Csv
dotnet add package Pragmatic.Documents.Csv.Generator
```

```csharp
[CsvSerializable]
public partial record Guest
{
    public int Id { get; init; }
    public string First { get; init; } = "";
    public string Last { get; init; } = "";
}

// Zero reflection: generated at compile time, as a nested Csv class
Guest.Csv.Write(stream, guests);
List<Guest> decoded = Guest.Csv.Read(stream);
```

---

## Runnable samples

Every renderer ships with a runnable `samples/` project you can `dotnet run`:

- [`Pragmatic.Documents.Pdf.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Pdf.Samples/README.md): basic / table / multi-page / landscape
- [`Pragmatic.Documents.Docx.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Docx.Samples/README.md): basic / table / multi-page with TOC / hyperlinks
- [`Pragmatic.Documents.Xlsx.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Xlsx.Samples/README.md): basic / multiple sheets / formulas + freeze / styled
- [`Pragmatic.Documents.Csv.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Csv.Samples/README.md): roundtrip / locale / formula injection / edge cases
- [`Pragmatic.Documents.Markup.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Markup.Samples/README.md): simple markup / data-bound / batch
- [`Pragmatic.Documents.Email.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Email.Samples/README.md): hero / columns / footer / markup-bound
- [`Pragmatic.Documents.Templating.Spreadsheet.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Templating.Spreadsheet.Samples/README.md): CSV / XLSX / in-memory data sources
