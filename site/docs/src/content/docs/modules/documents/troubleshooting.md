---
title: "Troubleshooting"
description: "Runtime errors and their fixes. Organised by symptom."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/troubleshooting.md
sidebar:
  order: 11
---
Runtime errors and their fixes. Organised by symptom.

---

## PDF

### "Unable to load DLL 'Pragmatic.Pdf.Native.dll'" at startup

The PDF renderer is native-backed. The native binary ships in the NuGet under `runtimes/{rid}/native/`. If it isn't resolved:

1. **Verify the RID is supported.** The package ships `win-x64`, `linux-x64` and `osx-arm64`. For other platforms, open an issue.
2. **Check your publish output.** For self-contained deployments the native binary must be next to the app (`dotnet publish -r win-x64 --self-contained`).
3. **Check Docker images.** Pragmatic.Documents.Pdf needs a base image with glibc on Linux; `alpine` (musl) is not supported today.

### `PdfRenderException`: "No fonts found on this machine"

The engine found no font at all. On Linux it reads only the folders fontconfig's configuration lists
(`/etc/fonts/fonts.conf`), so an image without the `fontconfig` package has none, even with font files
in `/usr/share/fonts`. Add both packages to the image that renders:

```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core
```

See *Styles and fonts* in [pdf-rendering.md](/modules/documents/pdf-rendering/).

### PDF opens but renders blank pages

- Did you call `.Page(p => ...)`? A `DocumentModel` with zero pages produces a 0-page PDF.
- Did every page builder add at least one node? An empty `.Page(p => { })` emits an empty page, which some viewers elide.

### The PDF uses another font than the one you set

The engine takes fonts from the machine that renders. A `FontFamily` that is not installed there
falls back to the default list (Noto Sans, Segoe UI, Helvetica, Arial, Liberation Sans, DejaVu Sans),
so the PDF comes out in a font that looks close but is not the one you named. Install the family where
the PDF is produced (the build agent or the container, not only your machine). `PdfResources` carries
images only, and `PdfRenderOptions.CustomFonts` is not wired into the engine yet. See *Styles and
fonts* in [pdf-rendering.md](/modules/documents/pdf-rendering/).

### PDF size is unexpectedly large

- Images are embedded as the bytes you pass in `PdfResources`. Compress them before adding them if
  they are larger than necessary.
- The fonts the engine uses are embedded as subsets: only the glyphs the document needs.

---

## DOCX

### Table of contents is empty in Word

The TOC is a field. Press **F9** in Word, or pass `UpdateFieldsOnOpen = true` in `DocxRenderOptions` so Word refreshes automatically:

```csharp
var options = new DocxRenderOptions { UpdateFieldsOnOpen = true };
DocxRenderer.RenderTo(fs, doc, options: options);
```

### Headings don't look like headings

DOCX heading levels map to the document's **Heading1..Heading9 paragraph styles**. If the target Word template has different styles for those names (or no styles at all), appearance will vary.

Force visual styles by combining a heading + a styled `TextNode`:

```csharp
.Heading("Title", level: 1)     // sets the outline level (for TOC)
.Paragraph(new TextNode {       // sets the visual appearance
    Content = "Title",
    Style = new NodeStyle { FontSize = 28, FontWeight = FontWeight.Bold, Color = "#1F497D" }
})
```

Or pass a `DocxTheme` to customise the built-in style definitions.

### Hyperlinks work but aren't coloured

The default hyperlink style in DOCX is defined by the template (`styles.xml`). The renderer emits the hyperlink correctly; if it shows up as black text, the consuming template has overridden the Hyperlink style.

---

## XLSX

### Formulas show as text (`=SUM(...)` instead of the computed value)

- You used `.Row("=SUM(A2:A10)")` instead of `.FormulaRow(...)`. Plain `.Row(value)` writes the string literally.
- Use `.FormulaRow("=SUM(A2:A10)")` or build a `Cell { Formula = "..." }`.

### Freeze / merge not respected

- `Freeze(rows, columns)` must be called **before** any rows: the freeze configuration is per-sheet metadata, not per-row.
- `Merge("A1", "C1")` coordinates must be valid cell references; malformed references throw an `ArgumentException` at render time.

### Opened XLSX has lost formatting applied in Excel

If you read → modify → write a file that used features the model doesn't represent (pivot tables, conditional formatting, charts), those are dropped on read. Keep the original file and apply modifications via a dedicated XLSX tool if you need to preserve them.

---

## CSV

### Cells with embedded commas appear split

The writer quotes cells that contain the delimiter, `"`, or newline. If you're seeing split cells in the *output*, inspect the actual bytes:

```
"hello, world","regular"    ✅ quoted correctly
hello, world,regular         ❌ malformed: shouldn't happen from CsvWriter
```

If `CsvWriter` produced unquoted output, file an issue with the input.

### Excel shows every row in column A

Excel's CSV auto-detection picks the delimiter based on the system locale. An Italian Excel expects `;` and treats `,`-separated CSV as "all one column".

Export with the locale-appropriate delimiter:

```csharp
var italian = new CsvOptions { Delimiter = ';', DecimalSeparator = ',' };
CsvWriter.Write(fs, book, italian);
```

### Encoding characters appear corrupted

UTF-8 without BOM is the default. Some Excel versions misinterpret UTF-8 without BOM. Force a BOM for broader compatibility:

```csharp
var options = new CsvOptions { WriteByteOrderMark = true };
```

---

## HTML email

### Outlook (desktop) renders the email with the wrong width

- Ensure `Width` is set (`new EmailBuilder().Width(600)`); Outlook respects the MSO-conditional wrapper table the renderer emits.
- Don't replace the output's wrapper table by post-processing the HTML: it's load-bearing for Outlook.

### Preheader shows up in the visible body

The preheader is hidden text at the top of the body; some clients with unusual CSS overrides may still show it. This is a known email-client quirk across the industry. You can reduce the preheader text to 1-2 words as a workaround.

### Images are blocked by default

Gmail, Outlook, and most mobile clients block remote images until the user clicks "show images". Always set `alt` text and make sure the email reads without images. The renderer emits `alt` from `Image(src, alt, ...)`.

---

## Markup

### `MarkupParseException: Root element must be <document>`

The PDX-Doc parser expects `<document>` as the root. If you have a different root, or no XML declaration, re-author with the right wrapper:

```xml
<document title="...">
  <page>
    <!-- ... -->
  </page>
</document>
```

### `XmlException` on parse

PDX uses **well-formed XML**. Stray `<` characters, unclosed elements, unescaped `&` all break parsing. Escape entity characters inside text:

```xml
<text>Q&amp;A session</text>   <!-- not "Q&A session" -->
```

### Expressions render literally as `{{ foo.bar }}`

You parsed the markup into a `DocumentTemplate` but didn't resolve it with data. Templates must go through a resolver:

```csharp
var template = PdxDocParser.Parse(markup);
var resolver = new DocumentTemplateResolver(PipeRegistry.Default.WithI18N());
var model = await resolver.ResolveAsync(template, data);   // ← this step
PdfRenderer.RenderTo(fs, model);
```

---

## Templating

### Missing data leaves the output empty

Intentional: missing properties render as `null`. Use `| default:"..."` for fallbacks, and inspect `resolver.Warnings` to see which expressions couldn't resolve.

### Pipe not found

`PipeRegistry.Default` has 5 core pipes (`uppercase`, `lowercase`, `trim`, `default`, `number`), and
the message lists them, so read it rather than this page. `currency`, `date` and `percent` come from
the I18N package (translation is the `t:` expression, not a pipe):

```csharp
var resolver = new DocumentTemplateResolver(PipeRegistry.Default.WithI18N());
```

For custom pipes, register explicitly:

```csharp
var pipes = PipeRegistry.Default.WithI18N().With(new TruncatePipe());
```

### Currency/date format looks wrong

Pipes honour the data context's culture. If you did not set one, it is `CultureInfo.InvariantCulture`
and not the server's culture.

```csharp
var data = new TemplateDataContext()
    .WithCulture(CultureInfo.GetCultureInfo("it-IT"))
    .AddSource("invoice", invoice);
```

---

## General

### "Types exist in multiple assemblies"

You probably installed `Pragmatic.Documents.Model` + a renderer but also took a transitive reference via another Pragmatic package. Run `dotnet list package --include-transitive` and ensure only one version of each model package is in the graph. Central Package Management (`Directory.Packages.props`) eliminates this category of issue.

### Output is non-deterministic

The renderers are deterministic given the same input. If byte-for-byte comparison fails, check:
- Embedded **creation date** metadata (set a fixed `DateTime` via the builder's metadata API when snapshot-testing)
- Image compression: if you load an image from disk, ensure the bytes haven't changed between runs
- For PDF, the fonts installed where it renders: two machines with different fonts produce different
  PDFs (see *Styles and fonts* in [pdf-rendering.md](/modules/documents/pdf-rendering/))

---

## Still stuck?

- Check the corresponding `samples/` project: it's a minimal, runnable demonstration
- Inspect the generated `obj/Generated/` folder if your issue might be source-generator related
- Open an issue with a reproducer: include the model (serialise with `DocumentSerializer.Serialize`) and the exact renderer / options used
