# Pragmatic.Documents

Document, email, spreadsheet, templating, and export tooling for Pragmatic.Design: render a single
document model to PDF, DOCX, HTML email, CSV, and XLSX.

`Pragmatic.Documents` is an umbrella module: a JSON-serializable document model plus renderers,
template resolvers, markup parsers, and data-source integrations. You build a `DocumentModel` (directly
or from markup/templates) and render it to whichever format you need: the model is the single source.

## Package map

| Package | Purpose |
|---------|---------|
| **Models** | |
| `Pragmatic.Documents.Model` | Page-oriented document model (`DocumentModel`, `DocumentBuilder`) |
| `Pragmatic.Email.Model` | HTML email model |
| `Pragmatic.Documents.Spreadsheet` | Spreadsheet model for XLSX/CSV |
| **Renderers and I/O** | |
| `Pragmatic.Documents.Pdf` | `DocumentModel` → PDF, through a native Typst compiler shipped in the package |
| `Pragmatic.Documents.Docx` | `DocumentModel` → DOCX |
| `Pragmatic.Documents.Email` | `EmailModel` → table-based HTML email with inline CSS (`EmailHtmlRenderer`), and its plain-text part (`EmailTextRenderer`) |
| `Pragmatic.Documents.Csv` / `.Xlsx` | Read/write CSV / XLSX |
| `Pragmatic.Documents.Csv.Generator` | Source generator: typed, AOT-safe CSV reader/writer for `[CsvSerializable]` types |
| `Pragmatic.Documents.Ooxml` | OOXML parts (namespaces, relationships, theme, core properties) used by the XLSX writer |
| **Templates** | |
| `Pragmatic.Documents.Markup` | PDX templates end to end: `IPdxTemplates` (a template by name, in the reader's language → document model, or mail subject/HTML/text), template sources, the PDX-Doc / PDX-Email parsers |
| `Pragmatic.Documents.Templating` | Expression engine: parser, evaluator, pipes, data context |
| `Pragmatic.Documents.Templating.I18N` | Date, currency, percent and `t:` pipes over `IStringLocalizer` |
| `Pragmatic.Documents.Templating.Spreadsheet` | XLSX and CSV files as template data sources |
| `Pragmatic.Documents.Templates` / `Pragmatic.Email.Templates` | Template types and resolvers → `DocumentModel` / `EmailModel` |

Add only the packages you need; each renderer depends on its model, not on the others.

## Quick Start

A document is a template and its data. The template is a file, changed, translated and reviewed
without a build, shipped inside the module that owns it:

```xml
<!-- templates/invoice.pdxdoc -->
<document title="{{ t:invoice.title }} {{ invoice.number }}">
  <page>
    <heading level="1">{{ t:invoice.title }} {{ invoice.number }}</heading>
    <text>{{ t:invoice.due }}: {{ invoice.total | currency }}</text>
  </page>
</document>
```

```xml
<!-- the module's .csproj -->
<EmbeddedResource Include="templates\*.pdxdoc;templates\*.pdxemail" />
```

```csharp
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templating.Data;

// once, in the module: every host that includes it registers the source
[assembly: PdxTemplates<BillingModule>]
// (a hand-built host, or another source: services.AddPdxTemplates(t => t.FromAssemblyOf<BillingModule>()))

// where the document is wanted: IPdxTemplates is injected
var data = new TemplateDataContext()
    .AddSource("invoice", new Dictionary<string, object?> { ["number"] = "INV-001", ["total"] = invoice.Total });

var document = await templates.DocumentAsync("invoice.pdxdoc", customer.Language, data, ct);
byte[] pdf = PdfRenderer.Render(document.Model);   // → PDF
// document.Warnings lists every name the template used and the data did not provide

var mail = await templates.EmailAsync("reminder.pdxemail", customer.Language, data, ct);
// mail.Subject, mail.Html, mail.Text: both bodies from one model
```

The language is the **reader's**, and it governs the `t:` translations and the `date` / `currency` /
`percent` pipes alike. Use `DocumentBuilder` / `EmailBuilder` when code decides the layout, or for what
the markup cannot express (styling, page-number fields, hyperlinks); see
[Templating](docs/templating.md), [Markup](docs/markup-parser.md) and
[Getting Started](docs/getting-started.md).

## Status

**Functional** within 1.0.0-alpha: PDF, DOCX, HTML email, CSV, and XLSX rendering. Parsing is currently
in-memory; see the [roadmap](../docs/ROADMAP.md) for streaming and large files.

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | The model-first architecture, choosing a package |
| [Getting Started](docs/getting-started.md) | Build a model and render it |
| [Templating](docs/templating.md) | Template resolvers and data binding |
| [Markup Parser](docs/markup-parser.md) | Building a model from markup |
| [PDF](docs/pdf-rendering.md) · [DOCX](docs/docx-rendering.md) · [Email](docs/email-rendering.md) · [CSV](docs/csv-io.md) · [XLSX](docs/xlsx-rendering.md) | Per-format rendering and options |
| [Common Mistakes](docs/common-mistakes.md) · [Troubleshooting](docs/troubleshooting.md) | Pitfalls and fixes |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem. See [Licensing](../docs/LICENSING.md).
Pragmatic.Documents is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
