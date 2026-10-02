---
title: "PDF Rendering"
description: "The `Pragmatic.Documents.Pdf` package renders a `DocumentModel` to a PDF byte stream. It uses a native-backed renderer (Typst)."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/pdf-rendering.md
sidebar:
  order: 7
---
The `Pragmatic.Documents.Pdf` package renders a `DocumentModel` to a PDF byte stream. It uses a native-backed renderer (Typst).

---

## API surface

```csharp
public static class PdfRenderer
{
    public static bool IsSupported { get; }
    public static byte[] Render(DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null);
    public static void RenderTo(Stream output, DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null);
    public static Task<byte[]> RenderAsync(DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null, CancellationToken ct = default);
    public static Task RenderToStreamAsync(Stream output, DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null, CancellationToken ct = default);
}

public static class PdfOperations
{
    public static byte[] Merge(params ReadOnlySpan<byte[]> pdfs);
    public static byte[] Split(ReadOnlySpan<byte> pdf, uint fromPage, uint toPage);
    public static uint GetPageCount(ReadOnlySpan<byte> pdf);
}
```

- `Render` materialises the whole PDF in memory. Fine for invoices, letters, one-offs.
- `RenderTo(Stream, ...)` streams directly to the destination. Use this for large reports.
- `GetPageCount` is a lightweight PDF-aware scan — no full parsing.

---

## Basic example

```csharp
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

var doc = new DocumentBuilder()
    .Title("Sample")
    .Page(p => p
        .Heading("Hello, PDF")
        .Paragraph(new TextNode { Content = "Rendered by Pragmatic." }))
    .Build();

using var fs = File.Create("sample.pdf");
PdfRenderer.RenderTo(fs, doc);
```

---

## Nodes supported

| Node | Effect in PDF |
|------|---------------|
| `TextNode` | Text run, formatted by its `NodeStyle` — see *Styles and fonts* |
| `HeadingNode` | Heading with auto-generated bookmark (for TOC); its `Children`, when set, replace `Content` |
| `ParagraphNode` | Block-level paragraph with inline children |
| `TableNode` | Table with header row, optional styling |
| `ImageNode` | Raster image (PNG, JPEG) with width/height/alt |
| `BarcodeNode` | Rendered barcode (default QR code; several types supported) |
| `HyperlinkNode` | Clickable link inside the PDF |
| `TocNode` | Table of contents built from heading bookmarks |
| `PageBreakNode` | Starts a new page |
| `Spacer`, `HorizontalRule` | Layout separators |
| `FieldNode` | Dynamic field — `FieldType.Page`, `NumPages`, `Date`, etc. |
| `BookmarkNode` | Named anchor for internal navigation |
| `FootnoteNode` | Footnote marker + footer entry on the same page |

Fields (`FieldType.Page`, `FieldType.NumPages`, `FieldType.Date`) are resolved during rendering; you don't supply current-page information.

---

## Styles and fonts

The engine applies `NodeStyle` where the DOCX renderer does:

- **On the text**, the run formatting: `FontFamily`, `FontSize` (points), `FontWeight`, `Italic`,
  `Underline`, `Strikethrough`, `VerticalPosition`, `Color` and `HighlightColor`.
- **It cascades, as in CSS.** Run formatting set on a `HeadingNode`, `ParagraphNode` or `HyperlinkNode`
  applies to the text inside it. A `TextNode`'s own style wins, property by property, where both set
  one. A heading's `Content` takes its heading's style; a heading built from `Children` renders them
  instead, each a run that completes the heading's style, as in DOCX.
- **On a block** (a `TextNode` on its own, a `ParagraphNode`, `HeadingNode`, `HyperlinkNode` or
  `ImageNode`), `TextAlign`. Alignment does not cascade: a `TextNode` inside a paragraph takes the
  paragraph's alignment, not its own.

```csharp
page.Add(new ParagraphNode
{
    Style = new NodeStyle { FontFamily = "Georgia", Color = "#666" },
    Children =
    [
        new TextNode { Content = "Due " },                                                    // Georgia, grey
        new TextNode { Content = "today", Style = new NodeStyle { Color = "#C00000" } },    // Georgia, red
    ]
});
```

`Color` is a hex value, with or without the `#` (`#666`, `1F4E79`). `HighlightColor` is a hex value or
an OOXML highlight name (`yellow`, `lightGray`, `none`). A colour that is neither fails the render with
a `PdfRenderException` instead of being dropped.

The rest of `NodeStyle` is layout the PDF engine does not map: margins and padding, borders,
background, width and height, line height, first-line indent, letter spacing — and a style on a
`TableCell` or `TableRow`. DOCX applies the paragraph spacing, indents, line height and letter spacing,
and a cell's shading and vertical alignment.

**Fonts** are the ones installed on the machine that renders; the engine embeds those a document uses.
`PdfRenderOptions.CustomFonts` is not wired into the engine yet, and setting it throws
`NotSupportedException`. Text with no `FontFamily` is set at 11 pt in the first of
Noto Sans, Segoe UI, Helvetica, Arial, Liberation Sans and DejaVu Sans that the machine has. A
`FontFamily` the machine does not have falls back to the same list.

On Linux the engine sees only the font folders that fontconfig's configuration lists
(`/etc/fonts/fonts.conf`). An image without the `fontconfig` package has none, even with font files
installed. There a render fails with a `PdfRenderException` that starts "No fonts found on this
machine" rather than producing a PDF with no font in it. Install both packages in the image that
renders:

```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core
```

---

## Page configuration

```csharp
var doc = new DocumentBuilder()
    .Size(PageSize.A4)            // A4, A3, A5, Letter, Legal, Custom
    .Landscape()
    .WithMargins(new Margins { Top = 20, Right = 20, Bottom = 20, Left = 20 })   // or Margins.Default / Narrow / None
    .Page(p => { /* ... */ })
    .Build();
```

Margins and node dimensions are in **millimetres**. `PageSize.A4` is 210 × 297 mm; `Margins.Default` is 25 mm on every side.

### Headers and footers

```csharp
.Page(p => p
    .Header(
        new TextNode { Content = "Pragmatic Quarterly Report", Style = new NodeStyle { FontSize = 9, Color = "#666" } })
    .Footer(
        new TextNode { Content = "Page " },
        new FieldNode { FieldType = FieldType.Page },
        new TextNode { Content = " of " },
        new FieldNode { FieldType = FieldType.NumPages })
    .Heading("Introduction")
    .Paragraph(...))
```

Header and footer nodes repeat on every page of the same `Page` block.

---

## Tables

```csharp
.Table(t => t
    .HeaderRow("Item", "Quantity", "Unit Price", "Total")
    .Row("Consulting", "10 h", "€150", "€1500")
    .Row("Implementation", "40 h", "€120", "€4800")
    .Row("QA", "12 h", "€90", "€1080"))
```

Tables size their columns automatically. For manual widths or per-cell styling, build the `TableNode` / `TableCell` directly rather than via the `HeaderRow`/`Row` shortcuts.

---

## Table of contents

```csharp
.Page(p => p
    .Toc(maxLevel: 3, title: "Contents"))
.Page(p => p
    .Heading("Chapter 1", level: 1)
    .Paragraph(...)
    .Heading("Section 1.1", level: 2)
    .Paragraph(...))
.Page(p => p
    .Heading("Chapter 2", level: 1)
    .Paragraph(...))
```

The TOC is generated during rendering by walking the heading bookmarks that appeared before it. Each entry includes the heading text and the target page number.

Limitations:
- `TocNode` must appear **before** the headings it references in document order
- Page numbers are resolved after layout, so very long headings that wrap differently than expected may shift page numbers

---

## Images

`PdfResources` is a dictionary of PNG bytes by name, and a node names the image as `resource:name`:

```csharp
var resources = new PdfResources { ["logo"] = File.ReadAllBytes("assets/logo.png") };

var doc = new DocumentBuilder()
    .Page(p => p.Image("resource:logo", width: 40, height: 20))
    .Build();

PdfRenderer.RenderTo(fs, doc, resources);
```

`PdfResources` holds images only — it has no font entries. An image the engine cannot find fails the
render with a `PdfRenderException`; see *Known limitations* for the sources it does not accept.

---

## Barcodes and QR codes

```csharp
.Barcode("https://pragmaticdesign.net", type: BarcodeType.QrCode)
.Barcode("1234567890128", type: BarcodeType.Ean13)
```

Available types include `QrCode`, `Code128`, `Code39`, `Ean13`. The barcode renders at a default size; set it explicitly via `BarcodeNode { Width = 120, Height = 120 }`.

---

## Performance

- For **large reports** (many pages, embedded images) use `RenderTo(Stream, ...)` instead of `Render` so you don't hold the whole output in memory.
- The renderer is deterministic — the same model produces byte-identical output (modulo creation dates), which makes PDFs snapshot-testable.
- Typical single-page invoice renders in ~5-15ms on modern hardware.

---

## Platform support

The PDF renderer is backed by a native library (`Pragmatic.Pdf.Native`). The package ships it for
**`win-x64`**, **`linux-x64`** and **`osx-arm64`**. On Linux the machine also needs fontconfig and a font
package — see *Styles and fonts*. The macOS binary is built and stamped by the `PDF Native` CI workflow on
a macOS runner, where its Rust unit tests run; no .NET test renders a PDF on macOS yet.

On any other platform the native library is not found and a
render call throws a `PdfRenderException` that names the platform, rather than a raw
`DllNotFoundException`. Probe availability before rendering to degrade gracefully:

```csharp
if (PdfRenderer.IsSupported)
    PdfRenderer.RenderTo(stream, doc);
else
    // fall back to DOCX, or surface a clear "PDF unavailable on this platform" message
    DocxRenderer.RenderTo(stream, doc);
```

---

## Known limitations

- No full CSS/HTML input — feed the document model directly.
- No form fields (fillable PDFs are out of scope).
- No digital signatures — sign with a separate step after rendering.
- No PDF/A profile tagging yet.
- The layout half of `NodeStyle` — spacing, borders, background, line height — is not applied; see
  *Styles and fonts*.
- `ImageNode.Source` for PDF must be a `resource:name` (from `PdfResources`). A `data:` URI, a file
  path, a remote URL or a name that is not in the resources fails the render with a
  `PdfRenderException`. DOCX also takes a base64 `data:` URI; only Email takes a URL, which the mail
  client fetches.

---

## Related

- [docx-rendering.md](/modules/documents/docx-rendering/) — same document model, DOCX output
- [markup-parser.md](/modules/documents/markup-parser/) — `.pdxdoc` markup → `DocumentTemplate`
- [templating.md](/modules/documents/templating/) — bind data into templates before rendering
