---
name: pragmatic-use-documents
description: Use when generating a PDF, DOCX, mail body, XLSX or CSV (invoice, letter, report, export) or editing a .pdxdoc/.pdxemail (Pragmatic.Documents, PDX templates, IPdxTemplates, fluent builder). Sending is pragmatic-use-email.
---

# Pragmatic Use Documents

**Covers:** Produce PDF, DOCX, e-mail bodies and XLSX/CSV exports with Pragmatic.Documents: PDX templates (.pdxdoc, .pdxemail) rendered by IPdxTemplates in the reader's language, the fluent builder, typed CSV; no Office, no browser.

Everything here produces a **model**: `DocumentModel` (pages), `EmailModel` (mail sections) or
`SpreadsheetModel` (sheets), and a renderer turns the model into bytes or HTML. How you get the model
is the choice that matters:

| You need | Use | Why |
|---|---|---|
| A letter, invoice, report, contract, notification mail: wording and layout somebody will want to change | **A PDX template** (`.pdxdoc` / `.pdxemail`) resolved against data | The text lives in a file, not in C#: it changes without a build, an organisation can upload its own, and a translator reads it |
| A layout computed by code: a variable number of sections driven by rules, styling per value, page numbers, hyperlinks, footnotes, a table of contents | **The builder** (`DocumentBuilder`, `EmailBuilder`) | Everything the model can express, from code; the markup covers the common shapes, not all of them |
| A table the user opens in Excel | `SpreadsheetBuilder` + `XlsxRenderer`, or CSV | Page layout is the wrong model for tabular data |
| A list of typed rows to/from CSV | `[CsvSerializable]` (source-generated) | No reflection, AOT-safe |

**Default to the template.** Reach for the builder when the markup cannot say it (see *What the markup
cannot express* below), not because C# feels more familiar.

## Packages

| Path | Packages |
|---|---|
| Templates → documents and mail | `Pragmatic.Documents.Markup`: `IPdxTemplates`, the template sources, the two parsers; it brings the resolvers, the pipes, the mail renderers |
| … → PDF / DOCX | `Pragmatic.Documents.Pdf` (native) / `Pragmatic.Documents.Docx` |
| Spreadsheet/CSV as template data | `Pragmatic.Documents.Templating.Spreadsheet` |
| Builder → PDF/DOCX | `Pragmatic.Documents.Model`, `.Pdf` / `.Docx` |
| Exports | `Pragmatic.Documents.Spreadsheet` + `.Xlsx` / `.Csv`; `Pragmatic.Documents.Csv.Generator` for typed rows |

There is no `Pragmatic.Documents` package: the folder is the module, the packages are the pieces.
⚠️ `Pragmatic.Email.Model` and `Pragmatic.Email.Templates` belong to this module (they are the mail's
**content**); `Pragmatic.Email` is the separate module that **sends** it.

## A template is one call: `IPdxTemplates`

A template by name, its data, and the reader's language: back comes the document model, or a mail's
subject, HTML and plain text, with what the template asked for and did not get.

```csharp
// the module ships its templates inside its assembly
<ItemGroup>
  <EmbeddedResource Include="templates\*.pdxdoc;templates\*.pdxemail" />
</ItemGroup>

// …and declares them, once: every host that includes the module registers the source itself
[assembly: PdxTemplates<BillingModule>]   // any public type of the module; Pragmatic.Documents.Markup
```

A host that is not generated, or templates from a directory, a database or a tenant's storage, register
with `services.AddPdxTemplates(t => t.FromDirectory(...).From(mySource))`; every call adds to the same
list, asked in order.

```csharp
[DomainAction]
public partial class IssueInvoiceAction : DomainAction<InvoiceDto>
{
    private IPdxTemplates _templates = null!;
    …
    var document = await _templates.DocumentAsync("invoice.pdxdoc", invoice.CustomerLanguage, data, ct);
    if (document.Warnings.Count > 0) { /* the template and its data drifted apart; see below */ }
    var pdf = PdfRenderer.Render(document.Model);               // or DocxRenderer, from the same model

    var mail = await _templates.EmailAsync("overdue-reminder.pdxemail", invoice.CustomerLanguage, data, ct);
    // mail.Subject, mail.Html, mail.Text → pragmatic-use-email / pragmatic-use-notifications
}
```

The data is a `TemplateDataContext` of named roots:

```csharp
var data = new TemplateDataContext()
    .AddSource("invoice", new Dictionary<string, object?>
    {
        ["number"] = invoice.Number,
        ["issuedOn"] = invoice.IssuedOn,
        ["gross"] = invoice.GrossTotal,          // a Money: {{ invoice.gross | currency }} writes its own currency
    })
    .AddSource("lines", invoice.Lines.Select(l => new Dictionary<string, object?>
    {
        ["description"] = l.Description, ["net"] = l.Net,
    }).ToList());
```

⚠️ **The language is a required parameter, and it is the reader's.** It governs the `t:` translations
**and** the `date` / `currency` / `percent` pipes. An invoice is in the customer's language, a mail in
the recipient's, rarely the caller's, and a job has no caller at all. (By hand, `data.WithCulture(...)`
does the same: the resolver translates in the context's culture, as the pipes format in it.) For "the
reader named no language" inject `IConfiguredCultures` and pass `cultures.Default.Code`.

⚠️ **`t:` resolves through the runtime `IStringLocalizer`**, so the module's `translations/*.json` must
reach it, which they do on their own when the module embeds them (`EmbedTranslations = true`, the
default: the host registers the generated provider); otherwise copied to a folder of the module's own and
added with `i18n.AddJsonTranslations(...)` in every
host that includes the module (`pragmatic-use-i18n`). Missing, every `t:` renders its key; through
`IPdxTemplates` each one is also a warning (`t:{key}`), which is why an empty `Warnings` is worth asserting.

⚠️ **`Warnings` is the only thing that tells you a template and its data drifted apart.** A name the
template uses and the data does not carry renders as nothing and adds a `TemplateWarning`; it never
throws. For the application's own template treat a warning as a defect (throw, or fail the test); for a
template a tenant uploaded, log it and render.

⚠️ **Dictionaries, not entities, as sources.** The names a template may use become the application's
public vocabulary for that document: a dictionary makes it a list somebody wrote down, instead of every
property (and navigation) the entity happens to have. Renaming a key breaks templates silently: a
warning, not an error. Typed objects work too, through reflection on a JIT runtime; under Native AOT a
property no accessor covers **throws**, so pass dictionaries there.

**Where templates come from.** `EmbeddedPdxTemplateSource` (a module's own; two modules cannot collide
on one output path as copied files do), `DirectoryPdxTemplateSource` (files an operator edits in place;
a name that climbs out of the directory is refused), `FirstFoundPdxTemplateSource(a, b)` (the first that
has it: a tenant's uploads before the application's), or your own `IPdxTemplateSource`. Names are file
names, with the folder when two folders share one (`"mail/welcome.pdxemail"`; a bare name two folders
share is refused). A template no source has throws `PdxTemplateNotFoundException` naming where it looked.

When the sources are yours to compose (per tenant), construct it:
`new PdxTemplates(new FirstFoundPdxTemplateSource(tenantUploads, applicationFiles), localizer)`.

## PDX-Doc (`.pdxdoc`): what the parser reads

```xml
<document title="Invoice {{ invoice.number }}" author="{{ company.name }}" lang="en" page-size="A4" margin="40">
  <page>
    <header><text>{{ company.name }}</text></header>
    <import src="letterhead.pdxdoc" />
    <heading level="1">{{ t:invoice.title(number=invoice.number) }}</heading>
    <text>{{ invoice.issuedOn | date:"d MMMM yyyy" }}</text>
    <text if="invoice.overdue">{{ t:invoice.overdue }}</text>
    <table data-source="invoice.lines">
      <column width="90">Description</column>
      <column width="20" align="right">Qty</column>
      <column width="30" align="right">Amount</column>
      <row-template>
        <cell>{{ item.description }}</cell>
        <cell>{{ item.qty }}</cell>
        <cell>{{ item.amount | currency:"EUR" }}</cell>
      </row-template>
    </table>
    <text>Total: {{ invoice.lines.Sum(amount) | currency:"EUR" }}</text>
    <footer><text>{{ company.vatId }}</text></footer>
  </page>
</document>
```

| Element | Attributes |
|---|---|
| `<document>` (root) | `title`, `author`, `lang`, `page-size` (`A3`, `A4`, `A5`, `Letter`, `Legal`), `orientation` (`portrait`, `landscape`), `margin` (points: `"40"`, `"40 20"` or `"top right bottom left"`), `page-data-source` + `page-item` (one page per item) |
| `<page>` | children; `<header>` / `<footer>` inside it |
| `<heading>` | `level` (1–6) |
| `<text>` | none: plain text with `{{ }}` |
| `<paragraph>`, `<container>` | child nodes |
| `<table>` | `data-source`, `repeat-header`; `<column width align>` (text = header cell), `<header>`, `<row>`, `<row-template>`, `<cell colspan rowspan>` |
| `<list>` | `ordered`, `data-source`; `<list-item>`, `<item-template>` |
| `<image>` | `src`, `alt`, `width`, `height` |
| `<barcode>` | `value`, `type` (`qr` default, `code128`, `code39`, `ean13`, `ean8`), `width`, `height` |
| `<hr>` / `<spacer>` / `<pagebreak>` | `thickness` / `height` / none |
| `<for-each>` | `source`, `item` (default `item`) |
| `<partial name>` / `<import src>` | the same partial, two spellings |

Any element takes `if="expression"` (skipped when false) and `for="line in invoice.lines"` (repeated,
with `line` in scope).

**`if` is false for `null`, `false`, a zero of any numeric type (`decimal` amounts included), an empty
string and an empty collection**, true for everything else: `if="invoice.balance"` hides a zero
balance, `if="invoice.lines"` an empty table.

⚠️ **The row variable of a data-bound `<table>` or `<list>` is always `item`**, and a `<row>` takes no
directives. Use `<for-each item="…">` or `for=` on other elements when you need a name of your own.

**What the parser does not know is refused** with a `MarkupParseException` naming it: an unknown
element, including ones older guidance showed (`<hyperlink>`, `<toc>`, `<field>`, `<bookmark>`,
`<footnote>`, `<page-break>`; it is `<pagebreak>`), and an attribute the element does not read:
`<text>` takes no `align`, `style` or `color`, and a misspelt `data-sorce` fails instead of leaving the
table unbound. Both markups; parse every template in a test so the refusal lands there, not in production.

## PDX-Email (`.pdxemail`)

```xml
<email subject="{{ t:mail.decision.subject(number=case.number) }}" preheader="{{ t:mail.decision.preheader }}"
       width="600" background="#ffffff" font-family="Arial, sans-serif" text-color="#333333">
  <partial name="mail-header" />
  <article>
    <text>{{ t:mail.decision.greeting(name=applicant.name) }}</text>
    <button href="{{ links.case }}" background="#1f497d">{{ t:mail.decision.open }}</button>
    <divider />
  </article>
  <row>
    <col width="6"><text>{{ organization.name }}</text></col>
    <col width="6" valign="middle"><image src="{{ organization.logo }}" alt="" width="120" /></col>
  </row>
  <footer><text align="center" color="#666666" size="12">{{ organization.address }}</text></footer>
</email>
```

- Sections: `<hero>` (centred), `<row>` with `<col width="1..12" valign padding>` (a 12-column grid),
  `<article>`, `<footer>`; any other top-level element becomes a one-element section. `background` and
  `padding` on sections.
- Content: `<heading level align color>`, `<text align color size>`, `<button href background color
  radius font-size align>`, `<image src alt width height link align>`, `<spacer height>`,
  `<divider color thickness>`, `<table data-source border padding>` with `<column>`, `<row>`,
  `<row-template>`, `<cell bold color align colspan>`; `<partial>` / `<import>`; `if` / `for` on any.
- ⚠️ `<section>`, `<column>`, `<two-columns>`, `<social-bar>`, `<paragraph>` and `background-color` are
  **not** PDX-Email; they are refused. The builder (`EmailBuilder`) has `TwoColumns`, `SocialBar` and
  friends; the markup has `<row>`/`<col>`.

Rendered by `IPdxTemplates.EmailAsync`: `Subject` (an expression in the template, so the template owns
it), `Html` (table-based, inline CSS, Outlook-safe; values from the data are encoded) and `Text`, the
plain-text part from the **same** model (`EmailTextRenderer`), never a second copy of the words.
Sending is `pragmatic-use-email`; a notification takes `Subject`/`Text`/`Html` as its content.

## Expressions

| Form | Example |
|---|---|
| Path | `{{ invoice.customer.name }}`; no indexing: `items[0]` is a `TemplateParseException`, as is any text the grammar cannot place (quote a pipe argument with spaces) |
| Pipes | `{{ total \| currency:"EUR" }}`, `{{ at \| date:"d MMM yyyy" }}`, `{{ rate \| percent:1 }}`, `{{ name \| default:"n/a" }}`, `uppercase`, `lowercase`, `trim`, `number:"#,##0"` |
| Operators | `==` `!=` `<` `<=` `>` `>=` `&&` `\|\|` `!`, `+ - * / %`, parentheses |
| Conditional | `{{ paid ? "Paid" : "Due" }}`, `{{ note ?? "" }}`; for translated alternatives use two elements with `if=` (`t:` is recognised only at the start of an expression) |
| Aggregates | `{{ lines.Count }}`, `{{ lines.Sum(amount) }}`, `Avg`, `Min`, `Max` |
| Translation | `{{ t:key }}`, `{{ t:key(name=customer.name, count=lines.Count) }}` |
| Literals | `"text"`, `'text'`, numbers, `true`, `false`, `null` |

Compute in C# what is business logic (VAT, discounts, eligibility) and hand the template the result;
expressions are for presentation. A custom pipe is an `ITemplatePipe` added with
`PipeRegistry.Default.WithI18N().With(new MyPipe())`.

## Data sources

- `AddSource(name, value)`: a dictionary or an object.
- `AddSource(name, async ct => …)` is **lazy**: resolved only if the template names it, then cached.
  Use it for anything that costs a query or a storage read.
- Spreadsheets as data: `CsvFileDataSource`, `XlsxFileDataSource`, and the stream forms
  `CsvStreamDataSource(name, ct => openStream(ct))` / `XlsxStreamDataSource` for a file behind
  `IFileStorage`, which has no path. Call `ResolveAsync(ct)` inside a lazy source.
- `DataSourceCatalog` composes named providers (`Add`, `AddAsync`, `AddJsonFile(name, path, typeInfo)`,
  `AddSql`) into a context with `ToDataContext(culture)`.

## Partials and per-customer templates

`<import src="letterhead.pdxdoc" />` / `<partial name="mail-header" />` are looked up in the **same**
source as the template, piece by piece. Write a document partial as a one-page document (its first
page's content is what is imported), a mail partial as a mail.

The shape that lets each tenant replace wording without a deployment: a source of your own that reads the
tenant's uploads (a row pointing at a file in `IFileStorage`, through the tenant-filtered repository),
first in a `FirstFoundPdxTemplateSource` before the application's own. Because partials go through the
same chain, a tenant can replace only its letterhead. Record which source answered (your source knows)
since "it rendered" is true of both. The Casework example is this shape end to end.

## What the markup cannot express: use the builder

Styling per node (`NodeStyle`: bold, colour, font, size), page-number and date fields, hyperlinks,
bookmarks, footnotes, a table of contents, and layouts decided by code:

```csharp
var doc = new DocumentBuilder()
    .Title("Report")
    .Page(p => p
        .Heading("Quarterly report", level: 1)
        .Text("Revenue grew.")
        .Table(t =>
        {
            t.Column(width: 80).Column(width: 40, align: TextAlign.Right);
            t.Row("Region", "Revenue");
            foreach (var r in rows) t.Row(r.Region, r.Revenue.ToString("N2"));   // cells are strings
        }))
    .Build();
```

⚠️ Content hangs off `.Page(p => …)`, not off the document builder. `Paragraph(...)` takes inline nodes,
not a string; a line of text is `.Text("…")`. `EmailBuilder` is the same idea for mail (`Section`,
`Hero`, `TwoColumns`, `Article`, `Footer`, `SocialBar`).

A resolved template and a built model are the same `DocumentModel`: resolve the template for the
standard part and add to the model in code only when there is a real reason.

## Rendering

- `PdfRenderer.Render(model)` / `RenderTo(stream, model)`: static; a native Typst compiler ships in the
  package (win-x64, linux-x64, osx-arm64). Fonts come from the machine that renders.
- `DocxRenderer.Render(model)`: static; fonts are named, not embedded.
- Stream (`RenderTo`) anything large instead of allocating a `byte[]`.
- Persist bytes with `pragmatic-use-storage`; attach them to mail with `pragmatic-use-email`. Do not
  render a document twice for one event; attach the bytes you stored.

## Spreadsheets and CSV

```csharp
var book = new SpreadsheetBuilder()
    .Sheet("Invoices", s => s.HeaderRow("Number", "Total").FreezeRows(1).Row("INV-1", 120m))
    .Build();
XlsxRenderer.RenderTo(stream, book);
CsvWriter.Write(stream, book.Sheets[0]);   // CSV is one sheet
```

CSV formula-injection protection (`CsvOptions.FormulaProtection`) is **on by default**; leave it on for
anything a user can type.

Typed rows, generated at compile time:

```csharp
[CsvSerializable]
public partial record OrderRow
{
    [CsvColumn("Order no.")]                    public string Number { get; init; } = "";
    [CsvColumn("Date", Format = "dd/MM/yyyy")] public DateTime Date { get; init; }
    [CsvColumn(Ignore = true)]                 public string InternalNote { get; init; } = "";
    public decimal Total { get; init; }
}

OrderRow.Csv.Write(stream, rows);                // WriteToArray(rows) → byte[]
List<OrderRow> back = OrderRow.Csv.Read(stream);
```

Properties need `set`/`init` and the type a parameterless constructor (a positional record does not
compile). Round-trippable: string, numbers, bool, `DateTime`, `DateTimeOffset`, `Guid`, `TimeSpan`,
enums and their nullable forms; anything else (`DateOnly` included) is **PRAG1900**: change the type
or `Ignore = true`.

## Testing a template

Compose it through `IPdxTemplates` in the language under test and assert on the **model** (or the mail's
`Subject`/`Html`/`Text`) and on `Warnings` being empty, not on PDF bytes, whose text is compressed. Run
it with the ambient culture set to a **different** language than the one passed, so a pass means the
parameter did the work. One test per template that names every key it uses catches the silent-drop
cases above.

> Parsing and rendering are in-memory. Licensing: `Pragmatic.Documents` is PolyForm Small Business
> (free for small businesses).

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing example application (code
that compiles and that `Invoicing.IntegrationTests` exercises) and kept identical to it by the gate: the
invoice and reminder templates, the module declaring them, the code that composes the invoice in the
customer's language and the reminder mail with the PDF attached, and the Italian translations the
templates read.
