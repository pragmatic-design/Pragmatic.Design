# HTML Email Rendering

The `Pragmatic.Documents.Email` package renders an `EmailModel` to email-client-safe HTML. The model is defined in `Pragmatic.Email.Model` and uses an idiomatic section → column → node builder API.

---

## Why a dedicated email model?

HTML email is not HTML. The big differences:

| HTML | HTML email |
|------|------------|
| CSS classes, stylesheets | Inline styles (Outlook ignores `<style>` half the time) |
| Modern layout (flex, grid) | Nested `<table>`s: tables are the safe layout primitive |
| No fixed width | Wrapper width (typically 600px) for predictable rendering |
| Preheader in `<head>` | Hidden preheader as the first text element |
| Images rendered freely | Images often blocked: need `alt` text and fallback |

Pragmatic.Documents.Email abstracts all of this. You describe sections and columns; the renderer produces HTML that works in Gmail, Outlook (including MSO-conditional tables for 2007+), Apple Mail, iOS/Android clients, and webmail.

---

## API surface

```csharp
public sealed class EmailHtmlRenderer
{
    public string Render(EmailModel model);
    public void RenderTo(TextWriter writer, EmailModel model);
}

public sealed class EmailBuilder
{
    public EmailBuilder Subject(string subject);
    public EmailBuilder Preheader(string preheader);
    public EmailBuilder Language(string language);
    public EmailBuilder Width(int width);
    public EmailBuilder BackgroundColor(string color);
    public EmailBuilder WrapperBackgroundColor(string color);
    public EmailBuilder FontFamily(string fontFamily);
    public EmailBuilder FontSize(int fontSize);
    public EmailBuilder TextColor(string color);

    // Section types
    public EmailBuilder Section(Action<SectionBuilder> configure);
    public EmailBuilder FullWidthSection(Action<ColumnBuilder> configure, string? backgroundColor = null);
    public EmailBuilder Hero(Action<HeroBuilder> configure, string? backgroundColor = null);
    public EmailBuilder TwoColumns(Action<ColumnBuilder> left, Action<ColumnBuilder> right, ...);
    public EmailBuilder ThreeColumns(...);
    public EmailBuilder Article(Action<ArticleBuilder> configure, string? backgroundColor = null);
    public EmailBuilder Footer(Action<ColumnBuilder> configure, string? backgroundColor = null);
    public EmailBuilder SocialBar(IEnumerable<SocialLink> links, string? backgroundColor = null);

    public EmailModel Build();
}
```

---

## Minimal example

```csharp
using Pragmatic.Email.Model;
using Pragmatic.Documents.Email;

var email = new EmailBuilder()
    .Subject("Welcome to Pragmatic")
    .Preheader("Your account is ready.")
    .Width(600)
    .Section(s => s
        .Column(c => c
            .Heading("Welcome, Alice!")
            .Text("Thanks for signing up.")
            .Button("Get started", "https://app.acme.com/start")))
    .Build();

string html = new EmailHtmlRenderer().Render(email);
```

---

## Section types

### Generic section

Sections are the default container: they accept one or more columns.

```csharp
.Section(s => s
    .BackgroundColor("#ffffff")
    .Padding(24)
    .Column(c => c
        .Heading("Section heading")
        .Text("Section body.")))
```

### Hero

A hero has background colour / image and a prominent heading.

```csharp
.Hero(h => h
    .BackgroundImage("https://cdn.acme.com/banner.jpg")
    .Heading("Spring sale")
    .Text("Save up to 30%")
    .Button("Shop now", "https://shop.acme.com"))
```

### Two / three columns

Side-by-side columns that collapse to a vertical stack on narrow screens (responsive CSS, no media-query gymnastics in your code).

```csharp
.TwoColumns(
    left:  l => l.Heading("Feature A").Text("Description of feature A."),
    right: r => r.Heading("Feature B").Text("Description of feature B."))
```

### Article

A classic article block with a heading, body text, and optional CTA.

```csharp
.Article(a => a
    .Heading("What's new in 0.8")
    .Paragraph("40 modules, 42 samples, end-to-end Showcase.")
    .Button("Read the release notes", "https://pragmaticdesign.net/0.8"))
```

### Footer + social bar

```csharp
.Footer(f => f
    .Text("You're receiving this email because you signed up at acme.com.",
          color: "#666", align: EmailTextAlign.Center)
    .Text("123 Main Street, Lisbon, Portugal", color: "#666", align: EmailTextAlign.Center))
.SocialBar(new[]
{
    new SocialLink { Platform = "twitter", Url = "https://twitter.com/acme" },
    new SocialLink { Platform = "github",  Url = "https://github.com/acme" },
})
```

---

## Column building blocks

Inside a column you assemble `EmailNode`s via the `ColumnBuilder`:

| Method | Produces |
|--------|----------|
| `.Heading(content, level, align)` | `<h1..h6>` with inline style |
| `.Text(content, align, color)` | Paragraph with inline style |
| `.Image(src, alt, width, link)` | `<img>` with optional wrapping `<a>` |
| `.Button(text, href, bg)` | Bulletproof button (MSO-conditional) |
| `.Spacer(height)` | Vertical spacing row |
| `.Divider(color)` | Horizontal rule |

Each helper returns the builder, so you can chain them.

---

## Client compatibility

The renderer emits:

- **DOCTYPE**: HTML 4.01 Transitional (most compatible)
- **Meta tags**: charset UTF-8, viewport, colour-scheme
- **MSO conditionals**: `<!--[if mso]>` blocks for Outlook-specific layout
- **Inline CSS** on every element, no reliance on `<style>`
- **Preheader**: hidden text as the first body element
- **Fallbacks**: buttons are bulletproof (anchor + table + MSO VML), images have `alt` text, font stacks include email-safe fallbacks

Tested against Gmail (web + iOS + Android), Outlook 2016/2019/365, Apple Mail, iOS Mail, Yahoo Mail, Outlook.com. For older Outlook (2007-2010) MSO-conditional blocks keep the layout intact.

---

## Delivering the email

Pragmatic.Documents.Email **produces HTML**: it doesn't send email. `Pragmatic.Email` sends it (or any
SMTP library or provider SDK):

From a template (the usual case), `IPdxTemplates` (`Pragmatic.Documents.Markup`) resolves and renders in
one call, in the recipient's language:

```csharp
var mail = await templates.EmailAsync("reminder.pdxemail", recipient.Language, data, ct);

var message = new EmailMessageBuilder()
    .From(sender.Address, sender.Name)
    .To(recipient.Email, recipient.Name)
    .Subject(mail.Subject)
    .HtmlBody(mail.Html)
    .TextBody(mail.Text)             // the plain-text part, from the same model
    .Build();

await email.SendAsync(message, ct);  // IEmailSender
```

From a model you built or resolved yourself: `new EmailHtmlRenderer().Render(model)` for the HTML (it holds
no state, so keep one in a static field; `IEmailRenderer` is a contract nobody registers, so injecting it is
`PRAG1641`) and `EmailTextRenderer.Render(model)` for the text part. The text comes from the **model** (
headings, paragraphs, a button as `label: url`, a table a row per line, trusted markup without its
tags), not from stripping the HTML and not from a second template, so the two parts cannot drift.

In an application the model comes from a `.pdxemail` template ([markup-parser.md](markup-parser.md))
rather than from `EmailBuilder`: the wording is then a file that is translated and changed without a
build. `EmailBuilder` is for mail whose layout code decides.

---

## Limitations

- No support for AMP email (`<amp-carousel>`, forms): plain HTML email only.
- No built-in i18n: use `Pragmatic.Documents.Templating.I18N` + a template.
- No A/B test variants or preview images: those belong to your ESP.
- Dark-mode tuning is opt-in per design: the renderer uses `color-scheme: light dark` but doesn't auto-adjust colour choices.

---

## Related

- [markup-parser.md](markup-parser.md): `.pdxemail` markup → `EmailTemplate`
- [templating.md](templating.md): bind dynamic data into email templates
- [`Pragmatic.Documents.Email.Samples`](../samples/Pragmatic.Documents.Email.Samples/README.md): hero, two-column, footer, markup-bound
