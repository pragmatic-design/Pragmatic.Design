# Common Mistakes

Patterns that look reasonable but don't fit the module's design. Read before you fight the API.

---

## 1. Treating the folder name as a package name

`Pragmatic.Documents` is the umbrella module. You install the **specific** runtime package you need:

- `Pragmatic.Documents.Pdf` for PDF output
- `Pragmatic.Documents.Docx` for DOCX
- `Pragmatic.Documents.Xlsx` for XLSX
- `Pragmatic.Documents.Csv` for CSV
- `Pragmatic.Documents.Email` for HTML email

`dotnet add package Pragmatic.Documents` will fail — there is no package with that exact name.

---

## 2. Mixing `DocumentModel` and `EmailModel`

These are **intentionally** different models. HTML email has constraints that page layout doesn't share (inline CSS, table-based layout, 600-800px width, preheader text, MSO conditional sections). Forcing one into the other produces output that renders badly in email clients.

```csharp
// ❌ Building a DocumentModel, rendering as email
var doc = new DocumentBuilder().Page(p => p.Heading("Hi")).Build();
new EmailHtmlRenderer().Render(doc);  // no — wrong model type

// ✅ Use EmailModel for email
var email = new EmailBuilder().Section(s => s.Column(c => c.Heading("Hi"))).Build();
new EmailHtmlRenderer().Render(email);
```

---

## 3. Using `DocumentModel` for tabular exports

If the deliverable is "a table the user opens in Excel", use `SpreadsheetModel` + XLSX. `DocumentModel` is for **page-oriented** output. A table in a PDF is a fine use case; a table as the *entire* payload isn't.

```csharp
// ❌ One 10,000-row table in a PDF
var doc = new DocumentBuilder()
    .Page(p => p.Table(t => { foreach (var r in rows) t.Row(r.A, r.B); }))
    .Build();

// ✅
var book = new SpreadsheetBuilder()
    .Sheet("Data", s => { s.HeaderRow("A", "B"); foreach (var r in rows) s.Row(r.A, r.B); })
    .Build();
XlsxRenderer.RenderTo(fs, book);
```

---

## 4. Forgetting `RenderTo(Stream)` for large outputs

`Render(model)` allocates a full `byte[]`. For multi-megabyte PDFs or XLSX workbooks, stream instead:

```csharp
// ❌ 50MB PDF — pays the allocation twice (model + byte array)
var bytes = PdfRenderer.Render(doc);
File.WriteAllBytes("big.pdf", bytes);

// ✅
using var fs = File.Create("big.pdf");
PdfRenderer.RenderTo(fs, doc);
```

---

## 5. Expecting CSV to round-trip multi-sheet workbooks

CSV is a single flat table. If your `SpreadsheetModel` has multiple sheets and you call `CsvWriter.Write(book)`, only the first sheet is written. Iterate yourself or switch to XLSX.

```csharp
// Explicit, intent-clear alternative
foreach (var sheet in book.Sheets)
{
    using var fs = File.Create($"{sheet.Name}.csv");
    CsvWriter.Write(fs, sheet);
}
```

---

## 6. Turning off formula protection on a CSV somebody will open in Excel

Excel executes leading `=`, `+`, `-`, `@` as formulas. `CsvWriter` guards against it by default
(`CsvOptions.FormulaProtection`, on): a value starting with one of those characters is written so that
the spreadsheet reads it as text.

```csharp
// ❌ Only for a CSV that no person opens and whose consumer needs the raw leading character
CsvWriter.Write(fs, book, new CsvOptions { FormulaProtection = false });
```

Leave it on for every CSV with user-supplied content.

---

## 7. Treating template expressions as C# code

`{{ expr }}` is a **small** expression language, not C#. It has property paths, pipes, comparisons,
`&&`/`||`/`!`, arithmetic, `cond ? a : b`, `??` and aggregates (`lines.Sum(amount)`) — see
[templating.md](templating.md#operators). It does not have:

- method calls (`invoice.GetTotal()`)
- indexing (`items[0]`)

Both are refused with a `TemplateParseException` when the template is parsed.

And that it *can* compute is not a reason to put business rules in a template. VAT, discounts and
eligibility belong to the domain, computed once and tested there; a template that recomputes them is a
second copy that will disagree with the first:

```csharp
// ❌ {{ price * 1.22 | currency:"EUR" }} — the VAT rate now lives in a text file
// ✅ compute in the domain, present in the template
data.AddSource("invoice", new Dictionary<string, object?> { ["net"] = net, ["vat"] = vat, ["total"] = total });
```

Then bind `{{ invoice.total | currency:"EUR" }}`.

---

## 8. Missing data in templates surfaces as empty string, not an error

By design, `{{ customer.nonexistent }}` renders as nothing rather than throwing. This keeps templates robust in the face of optional fields, but it hides typos.

Always check `data.Warnings` — the warnings live on the data context — after `ResolveAsync`:

```csharp
var model = await resolver.ResolveAsync(template, data);
foreach (var warning in data.Warnings)
    TemplateGap(logger, warning.Path, warning.Message);   // a [LoggerMessage] method
```

Or use `| default:"…"` to make fallbacks explicit:

```xml
<text>Hello, {{ customer.firstName | default:"there" }}!</text>
```

---

## 9. Assuming `Heading` level controls font size in DOCX

In DOCX, `Heading(level: 1..6)` maps to the **Heading1..Heading6 built-in paragraph style** (levels above 6 are clamped to Heading6). The actual font size comes from the theme (`DocxTheme` or the viewer's defaults), not from a hard-coded value.

If you want a specific font size, use a `TextNode` with explicit `NodeStyle.FontSize`, not a heading.

---

## 10. Treating `TocNode` as a static table

`TocNode` in both PDF and DOCX is a **field**. Content viewers populate it when they open the file. If you inspect the raw DOCX XML and the TOC body looks empty, that's expected — Word fills it in.

For PDF the TOC is pre-populated with heading bookmarks at render time, so it's correct immediately.

---

## 11. Expecting a font to travel with the document

Neither renderer takes a font file: `DocxResources` and `PdfResources` hold images only.

- **DOCX** writes the family you set (`NodeStyle.FontFamily`, `DocxTheme.BodyFont` / `HeadingFont`)
  by name and embeds nothing. The machine that opens the file uses its own copy, or substitutes one.
- **PDF** takes the family from the machine that **renders**, and embeds it, so the reader sees what
  the renderer had. A family that machine does not have falls back to the engine's default list.

```csharp
new TextNode { Content = "Acme", Style = new NodeStyle { FontFamily = "Acme Serif" } }
// DOCX: "Acme Serif" by name — present on the reader's machine or substituted there.
// PDF:  "Acme Serif" if the rendering machine has it, else the default list
//       (see pdf-rendering.md, Styles and fonts).
```

---

## 12. Markup parser expects XML, not Markdown

PDX-Doc looks vaguely HTML-ish because it's XML-based. Markdown-style (`# Heading`, `**bold**`, `| a | b |`) does **not** parse.

```xml
<!-- ✅ -->
<heading level="1">Title</heading>
<text style="bold">Important</text>

<!-- ❌ -->
# Title
**Important**
```

---

## 13. Sending HTML email without a plain-text alternative

Some clients show only the text part; spam filters score higher when both parts exist; accessibility tools prefer plain text.

```csharp
var mail = await templates.EmailAsync("reminder.pdxemail", recipient.Language, data, ct);

var message = new EmailMessageBuilder()
    /* From, To */
    .Subject(mail.Subject)
    .HtmlBody(mail.Html)
    .TextBody(mail.Text)            // same model; EmailTextRenderer.Render(model) for a model of your own
    .Build();
```

---

## Related

- [troubleshooting.md](troubleshooting.md) — runtime errors and their fixes
- [concepts.md](concepts.md) — the architecture rationale
