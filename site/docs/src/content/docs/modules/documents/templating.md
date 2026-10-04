---
title: "Templating: Expressions, Pipes, Data Context"
description: "The `Pragmatic.Documents.Templating` package is the expression engine shared between PDX-Doc and PDX-Email templates. It evaluates `{{ ... }}` expressions again"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Documents/docs/templating.md
sidebar:
  order: 8
---
The `Pragmatic.Documents.Templating` package is the expression engine shared between PDX-Doc and PDX-Email templates. It evaluates `{{ ... }}` expressions against a `TemplateDataContext`, applies pipes, and emits the resolved values back into the template.

---

## The three building blocks

```
{{expression}}        →  Evaluator
   |                      |
   ▼                      ▼
TemplateDataContext   PipeRegistry
(named data sources)  (format / transform)
```

| Component | Purpose |
|-----------|---------|
| `TemplateDataContext` | Named roots (`invoice`, `customer`, `company`) with property accessors |
| Expression evaluator | Parses `invoice.items[0].price | currency:"EUR"` into a pipeline |
| `PipeRegistry` | Catalogue of pipes; default has `uppercase/lowercase/trim/default/number`, extensible |

---

## Expression syntax

### Property access

```
{{ invoice.number }}
{{ customer.address.city }}
```

There is no indexing: `{{ items[0].name }}` is a `TemplateParseException` naming `[0].name`. Hand the
template the value it needs (`firstItem`), or iterate. An expression is read to its end: text the
grammar cannot place, an unquoted pipe argument with a space in it (`date:dd MMM yyyy`, quote it), or a
string with no closing quote is an error, never a silently shorter expression.

Properties are resolved via `IPropertyAccessor`. Built-in accessors:

- `DictionaryPropertyAccessor` for `IDictionary<string, object?>`
- `ReflectionPropertyAccessor` for typed records and classes, on a JIT runtime only. Under Native AOT
  a property no registered accessor covers **throws**; pass dictionaries there.

You can register custom accessors (`context.WithAccessor(accessor)`) for JSON nodes, expando objects,
dynamic proxies, anything with a "property by name" contract.

### Operators

| Kind | Operators |
|------|-----------|
| Comparison | `==` `!=` `<` `<=` `>` `>=` |
| Logic | `&&` `\|\|` `!` |
| Arithmetic | `+` `-` `*` `/` `%` |
| Conditional | `cond ? a : b`, `value ?? fallback` |
| Grouping | `( … )` |
| Aggregates on a collection | `lines.Count`, `lines.Sum(amount)`, `Avg`, `Min`, `Max` |
| Literals | `"text"`, `'text'`, numbers, `true`, `false`, `null` |

A translation (`t:key(…)`) is recognised only as a whole expression, not inside one.

### Pipes

Pipes format or transform the value before it lands in the output:

```
{{ price | currency:"EUR" }}          <!-- I18N package -->
{{ decidedOn | date:"yyyy-MM-dd" }}   <!-- I18N package -->
{{ title | uppercase }}
{{ name  | default:"anonymous" }}
```

⚠️ The date pipe is called **`date`**; there is no `format` pipe. The names below are the ones the
registry answers to.

Pipes chain left to right:

```
{{ description | trim | uppercase }}
```

### Pipe arguments

```
{{ total | currency:"EUR" }}                 // literal string arg
{{ count | number:"#,##0" }}                 // format-string arg
```

Pipes with multiple arguments separate them with commas:

```
{{ value | clamp:0,100 }}
```

### Missing values

If a property doesn't exist in the data context, the expression returns `null` (not a runtime error). Use `| default:"fallback"` to substitute.

The data context records a `TemplateWarning` for each path that could not be followed (a source or a
property nobody provides; a property that is there and `null` is not a warning), so you can audit
which placeholders went unresolved:

```csharp
var model = await resolver.ResolveAsync(template, data);
foreach (var warning in data.Warnings)
    Console.WriteLine($"[warn] {warning.Path}: {warning.Message}");
```

---

## Built-in pipes

| Name | Arguments | Purpose |
|------|-----------|---------|
| `uppercase` | n/a | `foo` → `FOO` |
| `lowercase` | n/a | `FOO` → `foo` |
| `trim` | n/a | strip surrounding whitespace |
| `default` | `value` | fallback when input is null/empty |
| `number` | `format` | .NET numeric format string |

With `Pragmatic.Documents.Templating.I18N`:

| Name | Arguments | Purpose |
|------|-----------|---------|
| `currency` | ISO code | `1200` + `"EUR"` → `€1,200.00` |
| `date` | format | `DateTime` → formatted per culture |
| `percent` | decimals | `0.225` → `22.5%` |

> `currency`, `date`, and `percent` are **not** built into the core engine: they ship in the
> `Pragmatic.Documents.Templating.I18N` package and must be registered with `WithI18N()` (below).
> Without it, `{{ x | currency }}` throws and the message says so: *"Unknown pipe: 'currency'. It is
> one of the pipes Pragmatic.Documents.Templating.I18N adds (currency, date, percent): reference that
> package and build the registry with PipeRegistry.Default.WithI18N()."*

These pipes format using the culture from the data context (`data.Culture`), parsing string inputs
with the invariant culture.

Enable them when building the resolver:

```csharp
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;

var resolver = new DocumentTemplateResolver(PipeRegistry.Default.WithI18N());
```

Translation is **not** a pipe: use the `t:` expression syntax (`{{ t:welcome(name=customer.name) }}`)
after wiring an `IStringLocalizer` with `context.WithLocalizer(localizer)`.

---

## TemplateDataContext

Named data sources keep expressions unambiguous when multiple roots are in play.

```csharp
using Pragmatic.Documents.Templating.Data;

var data = new TemplateDataContext()
    .AddSource("invoice", new Dictionary<string, object?>
    {
        ["number"]    = "2026-042",
        ["date"]      = DateTime.UtcNow,
        ["subtotal"]  = 6000m,
        ["vat"]       = 1320m,
        ["total"]     = 7320m,
        ["items"]     = new object[] { /* the lines */ },
    })
    .AddSource("customer", new { Name = "Mario Rossi", VatId = "IT98765432101" })
    .AddSource("company",  companyProfile);
```

Each root is isolated: `invoice.total` and `customer.total` can coexist without collision.

### Typed vs dictionary sources

Both work on a JIT runtime. Dictionary sources use `DictionaryPropertyAccessor`; typed objects use
`ReflectionPropertyAccessor` (cached per type). Prefer dictionaries:

- the keys a template may use become a list you wrote down, instead of every property (and navigation)
  the object happens to have;
- under Native AOT there is no reflection fallback: a property no registered accessor covers throws.

### Async data sources

For a source that costs an I/O call, register a factory. It runs only if the template names the
source, once, and the result is cached for the rest of the resolution:

```csharp
data.AddSource("fees", async ct => await LoadFeeTableAsync(ct));
```

To compose several named providers (static values, async factories, JSON files, SQL queries), use a
`DataSourceCatalog` and turn it into a context:

```csharp
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Data.Providers;

var catalog = new DataSourceCatalog()
    .Add("company", companyProfile)
    .AddAsync("invoice", async ct => await LoadInvoiceAsync(id, ct))              // Func<CancellationToken, ValueTask<T>>
    .AddJsonFile("settings", "settings.json", AppJsonContext.Default.Settings);   // AOT-safe overload

var data = catalog.ToDataContext("it-IT");
```

A custom provider implements `IDataSourceProvider` (`Name`, `ValueType`, `ResolveAsync(ct)`) and is
added with `catalog.AddProvider(provider)`. Spreadsheets as sources are in
`Pragmatic.Documents.Templating.Spreadsheet`: `CsvFileDataSource`, `XlsxFileDataSource`, and the
stream forms `CsvStreamDataSource` / `XlsxStreamDataSource` for a file behind a storage abstraction.

---

## Resolving templates

```csharp
using System.Globalization;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Documents.Pdf;

var template = PdxDocParser.ParseFile("invoice.pdxdoc");
var resolver = new DocumentTemplateResolver(PipeRegistry.Default.WithI18N());

var data = new TemplateDataContext()
    .WithCulture(CultureInfo.GetCultureInfo("it-IT"))
    .AddSource("invoice", invoice)
    .AddSource("customer", customer);

var model = await resolver.ResolveAsync(template, data);

using var fs = File.Create("invoice.pdf");
PdfRenderer.RenderTo(fs, model);
```

For email templates use `EmailTemplateResolver` + `PdxEmailParser` with the same pattern.

---

## Custom pipes

Implement `ITemplatePipe`:

```csharp
using System.Globalization;
using Pragmatic.Documents.Templating.Pipes;

public sealed class TruncatePipe : ITemplatePipe
{
    public string Name => "truncate";

    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
    {
        if (input is not string s) return input;
        if (args.Count == 0 || !int.TryParse(args[0], out var max)) return s;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
```

Register it:

```csharp
var pipes = PipeRegistry.Default
    .WithI18N()
    .With(new TruncatePipe());

var resolver = new DocumentTemplateResolver(pipes);
```

Use it in templates:

```xml
<text>{{ article.title | truncate:80 }}</text>
```

---

## Culture awareness

The data context carries a `CultureInfo`. I18N pipes respect it:

```csharp
var data = new TemplateDataContext()
    .WithCulture(CultureInfo.GetCultureInfo("de-DE"))
    .AddSource("invoice", invoice);

// {{ invoice.total | currency:"EUR" }} → "1.200,00 €" (German formatting)
```

Without `WithCulture` the context uses `CultureInfo.InvariantCulture`.

**The context's culture governs the pipes and the translations alike.** `ITranslationResolver.Resolve`
receives it, and `WithLocalizer` asks the `IStringLocalizer` in it (`WithCulture`), so a context set to
`it-IT` writes Italian sentences around its Italian dates with no culture scope around the render. A
context with no culture set resolves in the ambient one.

Translation is the `t:` expression, not a pipe (see above). The context resolves it through an
`ITranslationResolver`: `WithLocalizer(localizer)` wraps an `IStringLocalizer` in one, and
`WithTranslationResolver` takes your own, which receives the context's culture:

```csharp
public sealed class ResxTranslationResolver(ResourceManager strings) : ITranslationResolver
{
    public string Resolve(string key, IReadOnlyDictionary<string, object?>? parameters, CultureInfo culture)
        => strings.GetString(key, Equals(culture, CultureInfo.InvariantCulture) ? CultureInfo.CurrentUICulture : culture) ?? key;
}

var data = new TemplateDataContext()
    .WithTranslationResolver(new ResxTranslationResolver(strings))
    .AddSource("invoice", invoice);
```

---

## Performance

- Expressions are **parsed once** per template load (cached inside the template).
- Property access is cached per accessor per type.
- Pipe lookup is a hashtable lookup.
- Resolving an invoice template with ~20 expressions + a 10-row data-bound table: ~0.5ms on modern hardware.

For templates evaluated thousands of times, prefer loading the template once at startup and reusing it: the resolver is stateless across calls.

---

## Related

- [markup-parser.md](/modules/documents/markup-parser/): PDX-Doc / PDX-Email structural syntax
- [pdf-rendering.md](/modules/documents/pdf-rendering/): render resolved document models
- [email-rendering.md](/modules/documents/email-rendering/): render resolved email models
- [`Pragmatic.Documents.Templating.Spreadsheet.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Templating.Spreadsheet.Samples/README.md): data-source catalogue and spreadsheet workflows
