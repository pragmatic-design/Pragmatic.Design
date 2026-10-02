---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Documents is split the way it is, how the packages fit together, and how to choose the right entry point for each use case"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Documents is split the way it is, how the packages fit together, and how to choose the right entry point for each use case.

---

## The Problem

.NET document generation typically means one of three things:

- A **large closed-source library** (iText, Aspose, Syncfusion) that does everything but is expensive, carries unclear licensing for commercial products, or drags a large native dependency
- A **managed renderer** for a single format (OpenXml SDK for DOCX, QuestPDF for PDF, ClosedXML for XLSX) — you end up with 4 different APIs for 4 different outputs
- A **templating engine** (Razor, Liquid, Handlebars) that covers binding but leaves actual rendering to you

Each path solves part of the problem. None of them gives you a consistent model-driven workflow across PDF, DOCX, XLSX, CSV, and HTML email.

## The Approach

Pragmatic.Documents is a **family of packages** organised around three intentional concepts:

1. **Models** — serialisable data structures that describe a document, an email, or a spreadsheet
2. **Renderers** — pure functions that take a model and produce a byte stream
3. **Templates and markup** — optional layer that builds models from external definitions bound to data

Each layer is independent. You can use the builders alone, or plug the markup parser and templating engine in front of them.

```text
┌──────────────┐
│   Markup     │   optional: author-friendly DSL (PDX-Doc, PDX-Email)
│  (.pdxdoc)   │
└──────┬───────┘
       │ parse
       ▼
┌──────────────┐
│   Template   │   optional: binds to data via pipes and expressions
└──────┬───────┘
       │ resolve(data)
       ▼
┌──────────────┐
│    Model     │   always: DocumentModel / EmailModel / SpreadsheetModel
│ (sealed rec) │
└──────┬───────┘
       │ render
       ▼
┌──────────────┐
│   Renderer   │   PDF, DOCX, XLSX, CSV, HTML email
│ (byte stream)│
└──────────────┘
```

---

## Three model families

Pragmatic.Documents keeps three model families **separate** because their layout constraints are fundamentally different.

| Family | Core package | Natural outputs | Layout unit |
|--------|--------------|-----------------|-------------|
| **Document** | `Pragmatic.Documents.Model` | PDF, DOCX | Page (with margins, headers, footers) |
| **Email** | `Pragmatic.Email.Model` | HTML email | Section (table-based, inline CSS) |
| **Spreadsheet** | `Pragmatic.Documents.Spreadsheet` | XLSX, CSV | Sheet (rows × columns, formulas, merges) |

Forcing all three into a single abstraction would either lose format-specific features (merged cells in spreadsheets, preheader text in emails, page breaks in documents) or expose every concept in every context. The split is a design choice, not an oversight.

### Document model

`DocumentModel` represents **page-oriented output**. Every page has a set of nodes (Heading, Paragraph, Table, Image, Barcode, TOC, PageBreak, Footnote, ...) plus optional header/footer templates. The same model renders to both PDF and DOCX.

```csharp
var doc = new DocumentBuilder()
    .Title("Invoice 2026-001")
    .Author("Pragmatic")
    .Size(PageSize.A4)
    .Page(p => p
        .Heading("Invoice", level: 1)
        .Paragraph(new TextNode { Content = "Thank you for your business." })
        .Table(t => t
            .HeaderRow("Item", "Qty", "Price")
            .Row("Design review", "1", "250.00")))
    .Build();
```

### Email model

`EmailModel` represents an **HTML email** — table-based layout, inline CSS, preheader, width constraints. The renderer emits email-client-safe HTML (MSO-compatible tables, no external CSS).

```csharp
var email = new EmailBuilder()
    .Subject("Welcome to Pragmatic")
    .Preheader("Your account is ready")
    .Width(600)
    .Section(s => s
        .Heading("Welcome!")
        .Paragraph("Click below to verify your email."))
    .Build();
```

### Spreadsheet model

`SpreadsheetModel` represents **tabular data across one or more sheets**, with support for column widths, row freezing, cell merges, formulas, and per-cell styling. The same model renders to both XLSX and CSV (CSV flattens formulas and drops styling).

```csharp
var book = new SpreadsheetBuilder()
    .Title("Guest Export")
    .Sheet("Guests", s => s
        .HeaderRow("Id", "First", "Last")
        .Row(1, "Ada", "Lovelace")
        .Row(2, "Grace", "Hopper"))
    .Build();
```

---

## Package map

### Models (zero-dep data types)

| Package | Type | Purpose |
|---------|------|---------|
| `Pragmatic.Documents.Model` | `DocumentModel`, `DocumentBuilder`, node types | Page-oriented document data |
| `Pragmatic.Email.Model` | `EmailModel`, `EmailBuilder`, section types | HTML email data |
| `Pragmatic.Documents.Spreadsheet` | `SpreadsheetModel`, `SpreadsheetBuilder`, `Sheet`, `Row`, `Cell` | Tabular data |

Models are pure records. They serialise to JSON out of the box, which makes them easy to cache, snapshot, or send over a wire.

### Renderers (model → bytes)

| Package | Input | Output | Implementation |
|---------|-------|--------|----------------|
| `Pragmatic.Documents.Pdf` | `DocumentModel` | PDF byte stream | native-backed |
| `Pragmatic.Documents.Docx` | `DocumentModel` | DOCX byte stream | managed OOXML |
| `Pragmatic.Documents.Xlsx` | `SpreadsheetModel` | XLSX byte stream | managed OOXML |
| `Pragmatic.Documents.Csv` | `SpreadsheetModel` or raw rows | CSV byte stream | managed |
| `Pragmatic.Documents.Email` | `EmailModel` | HTML string | managed |

Each renderer exposes a `RenderTo(Stream, Model, ...)` static method. PDF also accepts an optional `PdfResources` (images by name); DOCX accepts `DocxResources` (images by name) and `DocxRenderOptions` (page numbering, TOC behaviour).

### Readers

| Package | Capability |
|---------|------------|
| `Pragmatic.Documents.Xlsx.XlsxReader` | `Read(Stream) → SpreadsheetModel` |
| `Pragmatic.Documents.Csv.CsvReader` | `ReadAsModel(Stream, sheetName) → SpreadsheetModel` |

Renderers and readers share the same model, so roundtrips are idempotent for the model features the format supports.

### Templates and markup

| Package | Purpose |
|---------|---------|
| `Pragmatic.Documents.Templates` | `DocumentTemplate` — a `DocumentModel` with placeholders |
| `Pragmatic.Email.Templates` | `EmailTemplate` — an `EmailModel` with placeholders |
| `Pragmatic.Documents.Templating` | Expression engine, pipe registry, data context resolver |
| `Pragmatic.Documents.Templating.I18N` | Date / currency / percent pipes + translation integration |
| `Pragmatic.Documents.Templating.Spreadsheet` | Spreadsheet data sources: `AddCsvStream` / `AddXlsxStream` read from wherever you can open one — `IFileStorage`, a blob column, object storage — and `AddCsvFile` / `AddXlsxFile` are the convenience over them for a workbook the operator puts beside the application. ⚠️ A spreadsheet a tenant **uploads** has no path; use the stream form for it |
| `Pragmatic.Documents.Markup` | `PdxDocParser` (PDX-Doc) and `PdxEmailParser` (PDX-Email) |

### Shared infrastructure

| Package | Purpose |
|---------|---------|
| `Pragmatic.Documents.Ooxml` | Shared OOXML writer used by Docx and Xlsx (zip + content-types + relationships) |
| `Pragmatic.Documents.Csv.Generator` | AOT-safe typed CSV source generator (`[CsvSerializable]` on a partial type) |

---

## Rendering pipeline

Every output follows the same three-stage pipeline:

```
Build or parse → Model → Render
```

### Stage 1 — build or parse

- **Builder**: imperative C# API (`DocumentBuilder`, `EmailBuilder`, `SpreadsheetBuilder`). Best when you have full control and want strongly-typed code paths.
- **Markup parser**: `PdxDocParser.Parse(markup)` / `PdxEmailParser.Parse(markup)` reads a `.pdxdoc` / `.pdxemail` file into a template. Best when authors (designers, content teams) own the layout.

### Stage 2 — model

All three paths land on a fully-typed, immutable model. You can:
- serialise it with `DocumentSerializer` / `EmailSerializer` / `SpreadsheetSerializer` for caching or wire transfer (all three use source-generated, AOT-safe `System.Text.Json`; weakly-typed spreadsheet cell values round-trip with their CLR type preserved)
- inspect/modify it before rendering
- attach images via renderer-specific resources objects (`DocxResources`, `PdfResources`)

### Stage 3 — render

Renderers are pure functions: same model → same output bytes. No hidden state, no ambient environment.
(The one exception is the OOXML document-property timestamp: DOCX/XLSX stamp `created`/`modified` with
`DateTimeOffset.UtcNow` when you don't supply one — set `DocumentModel.CreatedDate` / `DocxRenderOptions.RenderTimestamp`
for byte-reproducible output.)

```csharp
// In-memory: byte[]
var pdfBytes = PdfRenderer.Render(model);

// Streaming: writes directly to an open stream
using var fs = File.Create("invoice.pdf");
PdfRenderer.RenderTo(fs, model);
```

The streaming API is preferred for large outputs (reports with thousands of rows) to avoid materialising the whole byte array in memory.

---

## Templates and data binding

Templates are a **thin layer on top of models** that adds:

- placeholders (`{{customer.name}}`)
- loops (`{% for item in items %}...{% endfor %}`)
- conditionals (`{% if total > 0 %}...{% endif %}`)
- pipes for formatting (`{{price | currency:'EUR'}}`, `{{date | format:'yyyy-MM-dd'}}`)

When you resolve a template with a data context, you get back a regular `DocumentModel`, `EmailModel`, or equivalent — so rendering proceeds exactly as it would for hand-built models.

See [templating.md](/modules/documents/templating/) and [markup-parser.md](/modules/documents/markup-parser/) for the full syntax.

---

## When to use what

| Situation | Start with |
|-----------|-----------|
| "I need a PDF from C# with full layout control" | `DocumentBuilder` + `Pragmatic.Documents.Pdf` |
| "Same content, two outputs (PDF + DOCX)" | `DocumentBuilder` + both renderers |
| "Tabular export from a query result" | `SpreadsheetBuilder` + `Xlsx` (or `Csv` for plain CSV) |
| "Transactional email (verification, reset, invoice)" | `EmailBuilder` + `Pragmatic.Documents.Email` |
| "Designers author `.pdxdoc` templates, runtime binds data" | `Pragmatic.Documents.Markup` + `Pragmatic.Documents.Templating` + the right renderer |
| "I have CSV fixtures I want to round-trip through a model" | `CsvReader.ReadAsModel` + `CsvWriter.Write` |
| "AOT app that serialises typed CSV rows" | `Pragmatic.Documents.Csv.Generator` with `[CsvSerializable]` |

---

## What this module does not do

- **No reporting designer** (no WYSIWYG authoring tool) — templates are text files.
- **No form filling** — the PDF renderer emits, it does not fill existing PDFs.
- **No digital signatures** — a PDF signing step is out of scope for this package.
- **No email delivery** — `Pragmatic.Documents.Email` produces HTML; send it with `Pragmatic.Email` or your SMTP library of choice.
- **No chart rendering** — embed pre-rendered images, or generate SVG/PNG separately.

These are deliberate boundaries. The module focuses on being a small, composable, model-driven rendering layer.

---

## Reading order

1. [getting-started.md](/modules/documents/getting-started/) — five concrete scenarios, each ~5 minutes
2. [pdf-rendering.md](/modules/documents/pdf-rendering/) — the PDF-specific details (resources, options, quirks)
3. [docx-rendering.md](/modules/documents/docx-rendering/) — DOCX-specific details (TOC auto-update, headers/footers)
4. [xlsx-rendering.md](/modules/documents/xlsx-rendering/) — XLSX formulas, styling, freeze panes, merges
5. [csv-io.md](/modules/documents/csv-io/) — CSV read/write, locale, formula injection
6. [email-rendering.md](/modules/documents/email-rendering/) — email client compatibility, preheader, sections
7. [markup-parser.md](/modules/documents/markup-parser/) — PDX-Doc and PDX-Email syntax reference
8. [templating.md](/modules/documents/templating/) — expressions, pipes, data context, custom pipe registration
9. [common-mistakes.md](/modules/documents/common-mistakes/) / [troubleshooting.md](/modules/documents/troubleshooting/)
