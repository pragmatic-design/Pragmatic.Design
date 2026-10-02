# PDX Markup

PDX markup is an XML-based DSL for authoring document and email templates. It was designed to be familiar to anyone who can read HTML, but constrained to the subset that maps cleanly to `DocumentModel` and `EmailModel`.

Two parsers live in `Pragmatic.Documents.Markup`:

- `PdxDocParser.Parse(markup)` → `DocumentTemplate`
- `PdxEmailParser.Parse(markup)` → `EmailTemplate`

Both also expose `ParseFile(path)` convenience methods.

---

## Why markup?

Authoring a `DocumentBuilder` in C# is great when the author is the developer. When a designer, content team, or localisation team owns the layout, a **text file** is the right surface:

- no C# toolchain required
- git-friendly diffs
- easy to version per-customer variants
- parseable by tools outside the runtime (snippet validators, linters, preview tools)

The markup is **declarative** — expressions live in `{{ ... }}` blocks, so a designer can author the structure and a developer provides the data.

---

## PDX-Doc — document markup

### Skeleton

```xml
<document title="Invoice {{invoice.number}}" author="{{company.name}}"
          page-size="A4" orientation="portrait" margin="40" lang="en">
  <page>
    <!-- content -->
  </page>
  <page>
    <!-- another page -->
  </page>
</document>
```

Root attributes:

| Attribute | Values | Maps to |
|-----------|--------|---------|
| `title` | text or `{{expr}}` | `DocumentTemplate.Title` |
| `author` | text or `{{expr}}` | `DocumentTemplate.Author` |
| `lang` | ISO code (`en`, `it-IT`) | `DocumentTemplate.Language` |
| `page-size` | `A3`, `A4` (default), `A5`, `Letter`, `Legal` | `DocumentTemplate.PageSize` |
| `orientation` | `portrait` (default), `landscape` | `DocumentTemplate.Orientation` |
| `margin` | points: `"40"`, `"40 20"` (vertical horizontal) or `"40 20 40 20"` (top right bottom left) | `DocumentTemplate.Margins` |
| `page-data-source` | a collection path | one page per item |
| `page-item` | a name (default `item`) | the item's name on each generated page |

Without `<page>` elements, the root's children are the content of a single page.

### Content elements

Inside `<page>` — or `<header>` / `<footer>` inside a page — you place nodes:

| Element | Maps to | Attributes / children |
|---------|---------|------------|
| `<heading>` | `HeadingNode` | `level` (1-6) |
| `<text>` | `TextNode` | none — plain text with `{{ }}` |
| `<paragraph>` | `ParagraphNode` | child nodes |
| `<container>` | a group of nodes | child nodes |
| `<spacer />` | `SpacerNode` | `height` (points, default 10) |
| `<hr />` | `HorizontalRuleNode` | `thickness` (default 0.5) |
| `<image />` | `ImageNode` | `src`, `alt`, `width`, `height` |
| `<barcode />` | `BarcodeNode` | `value`, `type` (`qr` default, `code128`, `code39`, `ean13`, `ean8`), `width`, `height` |
| `<pagebreak />` | `PageBreakNode` | — |
| `<table>` | `TableNode` | see below |
| `<list>` | `ListNode` | `ordered`, `data-source`; `<list-item>`, `<item-template>` |
| `<for-each>` | repeats its children | `source` (required), `item` (default `item`) |
| `<import src="…" />` / `<partial name="…" />` | a partial — see [Partials](#partials) | |

An element not in this table is refused with a `MarkupParseException` that names it, and so is an
attribute the table does not list — `<text>` takes no `align`, `style` or `color`. Hyperlinks, page-number fields, bookmarks,
footnotes, a table of contents and per-node styling exist in the model but not in the markup: build
those parts with `DocumentBuilder`.

### Directives

Every element takes two attributes:

```xml
<text if="invoice.overdue">This invoice is overdue.</text>
<heading level="3" for="line in invoice.lines">{{ line.description }}</heading>
```

- `if="expression"` — the element is skipped when the expression is false: `null`, `false`, a zero of
  any numeric type (`decimal` included, so `if="invoice.balance"` hides a zero balance), an empty
  string, or an empty collection (`if="invoice.lines"`). Everything else is true.
- `for="name in collection.path"` — the element is repeated once per item, with `name` in scope.

### Tables

Plain table:

```xml
<table>
  <row><cell>Item</cell><cell>Qty</cell><cell>Price</cell></row>
  <row><cell>Design review</cell><cell>1</cell><cell>€250</cell></row>
</table>
```

Data-bound table (repeats `<row-template>` once per item):

```xml
<table data-source="invoice.items">
  <column width="90">Description</column>
  <column width="20" align="center">Qty</column>
  <column width="25" align="right">Price</column>
  <row-template>
    <cell>{{item.description}}</cell>
    <cell>{{item.qty}}</cell>
    <cell>{{item.price | currency:"EUR"}}</cell>
  </row-template>
</table>
```

Inside `row-template` the loop variable is always `item`, whatever the data source is called (rows do
not take directives, so there is no `for` on a `<row>`). Columns with text
become the header row, repeated on each page unless `repeat-header="false"`; a `<cell>` takes
`colspan` and `rowspan`, and holds either text or child nodes.

⚠️ `data-source` and `<row-template>` go together: one without the other is refused when the template
is resolved.

### Expressions

Any attribute or text content can include `{{ expression }}` interpolation. See [templating.md](templating.md) for the expression syntax.

### Headers and footers

```xml
<page>
  <header>
    <text>{{company.name}} — Invoice {{invoice.number}}</text>
  </header>
  <footer>
    <text>Confidential — {{company.name}}</text>
  </footer>

  <heading level="1">{{invoice.number}}</heading>
  <!-- ... -->
</page>
```

A page number is a `FieldNode`, which the markup does not produce: add it to the resolved model, or
build that document with `DocumentBuilder`.

### Full data-bound example

```xml
<document title="Invoice {{invoice.number}}" author="{{company.name}}">
  <page>
    <heading level="1">{{company.name}}</heading>
    <text>{{company.address}}</text>
    <spacer />
    <heading level="2">Invoice {{invoice.number}}</heading>
    <text>Date: {{invoice.date | date:"dd MMMM yyyy"}}</text>
    <text>Customer: {{customer.name}} — VAT {{customer.vatId}}</text>
    <hr />
    <table data-source="invoice.items">
      <column width="90">Description</column>
      <column width="20" align="center">Qty</column>
      <column width="25" align="right">Price</column>
      <column width="25" align="right">Total</column>
      <row-template>
        <cell>{{item.description}}</cell>
        <cell>{{item.qty}}</cell>
        <cell>{{item.price | currency:"EUR"}}</cell>
        <cell>{{item.total | currency:"EUR"}}</cell>
      </row-template>
    </table>
    <hr />
    <text>Subtotal: {{invoice.subtotal | currency:"EUR"}}</text>
    <text>VAT 22%: {{invoice.vat | currency:"EUR"}}</text>
    <heading level="3">TOTAL: {{invoice.total | currency:"EUR"}}</heading>
  </page>
</document>
```

Resolve it with a data context (see next section).

---

## PDX-Email

PDX-Email is the email counterpart, resolved into an `EmailModel` and rendered by `EmailHtmlRenderer`.
The root is `<email>`; its children are sections.

```xml
<email subject="Welcome to {{company.name}}" preheader="Your account is ready."
       width="600" lang="en" background="#ffffff" font-family="Arial, sans-serif" text-color="#333333">
  <hero background="#1F497D">
    <heading color="#ffffff">Welcome, {{user.firstName}}!</heading>
    <text color="#ffffff">Thanks for signing up.</text>
  </hero>
  <row padding="24">
    <col width="8">
      <text>You're all set. Click the button below to get started:</text>
      <button href="{{verifyLink}}">Verify your email</button>
    </col>
    <col width="4" valign="middle">
      <image src="{{company.logo}}" alt="{{company.name}}" width="120" />
    </col>
  </row>
  <footer background="#eeeeee">
    <text align="center" color="#666666" size="12">© {{year}} {{company.name}}</text>
  </footer>
</email>
```

Root attributes: `subject`, `preheader`, `lang`, `width` (default 600), `background`,
`wrapper-background`, `font-family`, `font-size` (default 16), `text-color`.

Sections:

| Element | What it is | Attributes |
|---------|------------|------------|
| `<hero>` | one centred column | `background`, `padding` (default 30) |
| `<row>` | a 12-column grid of `<col>` | `background`, `padding`; `<col width="1..12" valign="top\|middle\|bottom" padding>` — no `<col>` means one full-width column |
| `<article>` | one column; a level-1 heading becomes level 2 | `background`, `padding` |
| `<footer>` | one column | `background`, `padding` (default 10) |
| anything else | a section holding that single element | |

Content, inside a section or a column:

| Element | Attributes |
|---------|------------|
| `<heading>` | `level`, `align` (`left`, `center`, `right`), `color` |
| `<text>` | `align`, `color`, `size` |
| `<button>` | `href`, `background`, `color`, `radius`, `font-size`, `align` |
| `<image>` | `src`, `alt`, `width`, `height`, `link`, `align` |
| `<spacer>` | `height` (default 20) |
| `<divider>` | `color`, `thickness` |
| `<table>` | `data-source`, `border`, `padding`; `<column width align>`, `<row background>`, `<row-template>`, `<cell bold color align colspan>` |
| `<partial name="…" />` / `<import src="…" />` | the partial of that name — see [Partials](#partials) |

`if` and `for` work on sections and on content, as in PDX-Doc.

⚠️ The markup has no `<section>`, `<column>`, `<two-columns>`, `<three-columns>`, `<social-bar>` or
`<paragraph>`, and colours are `background`, not `background-color`: those forms are refused with a
`MarkupParseException`. `EmailBuilder` has `TwoColumns`, `ThreeColumns` and `SocialBar`; in markup a `<row>` of
`<col>`s is the multi-column layout.

See [email-rendering.md](email-rendering.md) for what the renderer does with each section.

---

## Loading and parsing

```csharp
using Pragmatic.Documents.Markup;

var template = PdxDocParser.Parse(xmlString);
// or
var template = PdxDocParser.ParseFile("invoice.pdxdoc");
```

Markup that is not well-formed XML throws `XmlException`, with the line and position. A structural
error throws `MarkupParseException` naming what is wrong:

```
MarkupParseException: Root element must be <document>, got <documentz>
MarkupParseException: <for-each> requires 'source' attribute
MarkupParseException: Unknown element <section> in a document.
MarkupParseException: Unknown attribute 'data-sorce' on <table>. Known: if, for, data-source, repeat-header.
```

What the markup does not know is an error, never a silent drop: an unknown element, an attribute an
element does not read, and content beside the `<page>` elements of a document or the `<col>` elements
of a row.

---

## Partials

Large templates split into reusable fragments:

```xml
<!-- invoice.pdxdoc -->
<document title="Invoice {{invoice.number}}">
  <page>
    <import src="header.pdxdoc" />
    <heading level="1">Invoice {{invoice.number}}</heading>
    <!-- ... -->
    <import src="footer.pdxdoc" />
  </page>
</document>
```

`<import src="…"/>` resolves to the partial registered under that name. A partial is a
`DocumentPartialTemplate`; the parser has no fragment mode, so write the partial file as a one-page
document (`<document><page>…</page></document>`) and register its page's content:

```csharp
static DocumentPartialTemplate Partial(string file) => new()
{
    Name = file,
    Content = PdxDocParser.ParseFile(file).Pages[0].Content,
};

var resolver = new DocumentTemplateResolver()
    .WithPartial("header.pdxdoc", Partial("header.pdxdoc"))
    .WithPartial("footer.pdxdoc", Partial("footer.pdxdoc"));
```

Partials receive the same data context as the parent template.

### The same two spellings in both markups

`<import src="…" />` and `<partial name="…" />` are the same thing, and both parsers accept both: the
name is whatever the attribute carries, and what to make of it — a file name, a key — is the partial
provider's business. In an email a partial may sit inside a section or be a direct child of
`<email>`, where it becomes a section of its own.

⚠️ Both are honoured at both levels, so a mail written by somebody who learned the document markup
keeps its letterhead. An attribute-less `<import>` is refused, as is `<partial>` without its `name`.

---

## Resolving the template

Parsing gives you a template. To get a renderable `DocumentModel` / `EmailModel`, resolve it with data:

```csharp
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Pipes;

var resolver = new DocumentTemplateResolver(PipeRegistry.Default);
var data = new TemplateDataContext()
    .AddSource("invoice", invoice)
    .AddSource("company", company)
    .AddSource("customer", customer);

var model = await resolver.ResolveAsync(template, data);
using var fs = File.Create("invoice.pdf");
PdfRenderer.RenderTo(fs, model);
```

See [templating.md](templating.md) for the data context, pipes, and custom pipe registration.

---

## Editor support

A VS Code / Rider language server for PDX-Doc is on the roadmap. For now use XML IntelliSense — the schema validates structure, though expression syntax is intentionally free-form.

---

## Related

- [templating.md](templating.md) — expressions, pipes, data context
- [pdf-rendering.md](pdf-rendering.md) — render resolved document templates to PDF
- [email-rendering.md](email-rendering.md) — render resolved email templates to HTML
- [`Pragmatic.Documents.Markup.Samples`](../samples/Pragmatic.Documents.Markup.Samples/README.md) — simple, data-bound, and batch markup examples
