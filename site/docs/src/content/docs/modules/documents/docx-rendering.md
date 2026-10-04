---
title: "DOCX Rendering"
description: "The `Pragmatic.Documents.Docx` package renders a `DocumentModel` to a DOCX (Office Open XML / ISO/IEC 29500) byte stream. The implementation is pure managed: no"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/docx-rendering.md
sidebar:
  order: 4
---
The `Pragmatic.Documents.Docx` package renders a `DocumentModel` to a DOCX (Office Open XML / ISO/IEC 29500) byte stream. The implementation is pure managed: no native dependency, AOT-compatible.

---

## API surface

```csharp
public static class DocxRenderer
{
    public static byte[] Render(DocumentModel model,
        DocxResources? resources = null, DocxRenderOptions? options = null);
    public static void RenderTo(Stream output, DocumentModel model,
        DocxResources? resources = null, DocxRenderOptions? options = null);
    public static Task<byte[]> RenderAsync(DocumentModel model,
        DocxResources? resources = null, DocxRenderOptions? options = null, CancellationToken ct = default);
    public static Task RenderToStreamAsync(Stream output, DocumentModel model,
        DocxResources? resources = null, DocxRenderOptions? options = null, CancellationToken ct = default);
}
```

- `Render` materialises the full DOCX in memory.
- `RenderTo(Stream)` streams into the target.
- All of them accept optional `DocxResources` (images by name) and `DocxRenderOptions`. The class is
  static: there is nothing to construct.

---

## Basic example

```csharp
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Docx;

var doc = new DocumentBuilder()
    .Title("Quarterly report")
    .Author("Alice")
    .Page(p => p
        .Heading("Q1 2026 Review", level: 1)
        .Paragraph(new TextNode { Content = "Revenue grew 12% QoQ." })
        .Table(t => t
            .HeaderRow("Metric", "Q1", "Q4")
            .Row("Revenue", "€1.2M", "€1.07M")
            .Row("Headcount", "42", "38")))
    .Build();

using var fs = File.Create("report.docx");
DocxRenderer.RenderTo(fs, doc);
```

Open the file in Word, LibreOffice, or Google Docs.

---

## Shared model with PDF

The DOCX renderer accepts the **same** `DocumentModel` as the PDF renderer. Swap the output format by swapping the renderer:

```csharp
DocumentModel doc = BuildReport();

using (var pdf  = File.Create("report.pdf"))  PdfRenderer.RenderTo(pdf, doc);
using (var docx = File.Create("report.docx")) DocxRenderer.RenderTo(docx, doc);
```

Not every node maps identically; see the "Node mapping" section below.

---

## Node mapping

| Node | DOCX output |
|------|-------------|
| `TextNode` | `<w:r><w:t>` run with style from `NodeStyle` |
| `HeadingNode` | Heading paragraph using the `Heading1`..`Heading9` styles (bookmarked for TOC) |
| `ParagraphNode` | `<w:p>` paragraph |
| `TableNode` | `<w:tbl>` table with header row styling |
| `ImageNode` | Embedded image (PNG/JPEG) with DrawingML |
| `HyperlinkNode` | External hyperlink run |
| `TocNode` | Field-coded TOC (populated on open; see "Table of contents" below) |
| `PageBreakNode` | `<w:br w:type="page"/>` |
| `Spacer` | Empty paragraph with height |
| `HorizontalRule` | Paragraph with a bottom border |
| `FieldNode` with `FieldType.Page / NumPages / Date` | Field codes (`PAGE`, `NUMPAGES`, `DATE`) |
| `BookmarkNode` | `<w:bookmarkStart>` / `<w:bookmarkEnd>` pair |
| `FootnoteNode` | `<w:footnoteReference>` + footnote content |
| `BarcodeNode` | Rendered as an image (the renderer rasterises barcodes so they survive copy/paste) |

---

## Styles and fonts

`NodeStyle` splits into two kinds of property, and the PDF engine applies them the same way:

- **Run formatting** is written on each text run (`w:rPr`): `FontFamily`, `FontSize` (points), `FontWeight`,
  `Italic`, `Underline`, `Strikethrough`, `VerticalPosition`, `Color`, `HighlightColor` and
  `LetterSpacing` (mm).
- **Run formatting cascades, as in CSS.** Set on a `HeadingNode`, `ParagraphNode` or `HyperlinkNode`, it
  applies to the text inside it. A `TextNode`'s own style wins, property by property, where both set one.
- **A heading's text takes the heading's run formatting** on top of the `Heading{level}` style. For example,
  `new HeadingNode { Content = "Invoice", Style = new NodeStyle { FontSize = 20 } }` is 20 pt.
- **A link's text** keeps the `Hyperlink` character style and adds what cascades to it.
- **Paragraph formatting** is written on the paragraph (`w:pPr`) and does not cascade: `TextAlign`,
  `MarginTop`/`MarginBottom` (spacing), `LineHeight`, `MarginLeft`/`MarginRight` and `FirstLineIndent`
  (indents).

---

## Table of contents

DOCX TOCs are **field-coded**: the `TocNode` emits a `{ TOC \o "1-3" \h \z \u }` field with placeholder content that Word/LibreOffice refresh when the document is opened.

Enable explicit update on open via `DocxRenderOptions`:

```csharp
var options = new DocxRenderOptions { UpdateFieldsOnOpen = true };
DocxRenderer.RenderTo(fs, doc, options: options);
```

The renderer uses a heuristic to pre-populate the TOC text with estimated page numbers so the file looks right even in viewers that don't auto-update fields. Word overwrites this when it refreshes.

---

## Images

`DocxResources` is a dictionary of image bytes by name. A node names the image as `resource:name`,
or inlines it as a base64 `data:` URI:

```csharp
var resources = new DocxResources { ["logo"] = File.ReadAllBytes("logo.png") };

var doc = new DocumentBuilder()
    .Page(p => p.Image("resource:logo", width: 40, height: 20, alt: "Company logo"))
    .Build();

DocxRenderer.RenderTo(fs, doc, resources);
```

Each image is embedded as a part of the OOXML package. The renderer embeds bytes it is given: it does
not fetch URLs or read files. **An image it cannot resolve fails the render** with an
`InvalidOperationException` that names the source, the node's alt text, and the resource names that
were passed (a name that is not among them, no resources at all, a URL, or a `data:` URI that is not
valid base64). Dropping the image instead would let the call succeed with the picture missing from
the file.

---

## Themes

Set a `DocxTheme` to customise heading colours, default fonts, and body text colour:

```csharp
var options = new DocxRenderOptions
{
    Theme = new DocxTheme
    {
        PrimaryColor = "1F497D",   // H1-H2; SecondaryColor colours H3 and below
        BodyFont = "Calibri",
        HeadingFont = "Cambria",
    }
};

DocxRenderer.RenderTo(fs, doc, options: options);
```

---

## Headers, footers, page numbers

Headers and footers are per-document (not per-page) in DOCX. If the `DocumentModel` has `Header(...)` / `Footer(...)` on a page, the renderer uses them for the whole document.

```csharp
.Page(p => p
    .Header(new TextNode { Content = "Acme Corp · Q1 2026" })
    .Footer(
        new TextNode { Content = "Page " },
        new FieldNode { FieldType = FieldType.Page },
        new TextNode { Content = " of " },
        new FieldNode { FieldType = FieldType.NumPages }))
```

---

## Limitations

- No multi-column layout (single-column flow only).
- No advanced drawing shapes (tables and images are supported; SmartArt / charts are not).
- Custom paragraph styling is limited to `NodeStyle` properties; if you need complex styled paragraphs, use a designer template instead.
- Track-changes and comments are out of scope.

---

## Related

- [pdf-rendering.md](/modules/documents/pdf-rendering/): same model, PDF output
- [markup-parser.md](/modules/documents/markup-parser/): author templates in PDX-Doc markup
- [`Pragmatic.Documents.Docx.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Docx.Samples/README.md): runnable scenarios
