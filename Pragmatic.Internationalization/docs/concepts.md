# Architecture and Core Concepts

This guide explains **why** Pragmatic.Internationalization exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this before diving into the individual feature guides.

---

## The Problem

.NET has built-in globalization support through `CultureInfo`, `NumberFormatInfo`, and resource files (`.resx`). But when you try to build a real multilingual application, the gaps emerge quickly.

### Thread culture is fragile and global

```csharp
// Set culture for the current request
Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

// Later, an async continuation runs on a different thread
await httpClient.GetAsync(url);

// Now Thread.CurrentThread.CurrentCulture might be en-US again
var price = total.ToString("C"); // Wrong culture!
```

`CultureInfo.CurrentCulture` is thread-local. When the runtime schedules your continuation on a different thread pool thread, the culture resets. You can set `CultureInfo.DefaultThreadCurrentCulture`, but that is a process-wide setting that affects all requests -- a multi-tenant disaster.

### No concept of multi-scope culture

A real application often needs different cultures simultaneously within the same request:

- **UI culture**: Italian (`it-IT`) -- for labels and messages shown to the user
- **Data culture**: English (`en-US`) -- for API responses, database storage, and exports
- **Invoice culture**: German (`de-DE`) -- for a specific business document

.NET gives you `CurrentCulture` and `CurrentUICulture`. Two slots. For anything more, you are on your own.

### Resource files (.resx) do not compose

```xml
<!-- Resources/Strings.en.resx -->
<data name="Welcome" xml:space="preserve">
  <value>Welcome!</value>
</data>

<!-- Resources/Strings.it.resx -->
<data name="Welcome" xml:space="preserve">
  <value>Benvenuto!</value>
</data>
```

Resource files work, but they scatter translations across dozens of XML files with no way to validate completeness at build time. You can ship a release with half your Italian translations missing and not notice until a customer reports it. Plurals require manual `if/else` chains. And sharing translations across assemblies requires publishing satellite assemblies with specific folder structures.

### Money is not a first-class concept

```csharp
decimal price = 99.99m;
string currency = "EUR";
// No type safety: can accidentally add USD to EUR
// No culture-aware formatting
// No rounding rules per currency (JPY has 0 decimals, USD has 2)
```

Every e-commerce application reinvents `Money` as a struct with an amount and a currency code. Without it, currency mismatches are silent bugs, rounding is inconsistent, and formatting varies by developer.

### Plurals are language-specific and complex

```csharp
// English: simple
var message = count == 1 ? "1 item" : $"{count} items";

// Russian: count % 10 == 1 && count % 100 != 11 -> singular
//          count % 10 in 2..4 && count % 100 not in 12..14 -> few
//          else -> many
// Good luck maintaining that in every service.
```

The CLDR defines six plural categories (`Zero`, `One`, `Two`, `Few`, `Many`, `Other`) with language-specific rules. Russian has three plural forms based on the last two digits. Arabic uses all six categories. A correct implementation requires a rule engine, not `if/else`.

**The fundamental issue**: .NET provides low-level building blocks, but leaves the developer to wire them together correctly for every application. Culture context management, multi-scope cultures, compile-time translation safety, type-safe money, and CLDR plurals are all recurring needs that should be solved once.

---

## The Solution

Pragmatic.Internationalization provides a unified system with five pillars:

1. **I18NContext**: An `AsyncLocal`-based ambient context that flows across `await` boundaries, supports multiple culture scopes (UI, Data, custom), and synchronizes with .NET thread cultures.

2. **Money and CurrencyCode**: Value types that enforce same-currency arithmetic, provide ISO 4217 metadata, and format according to the ambient culture.

3. **Formatting**: Culture-aware extension methods and an injectable `GlobalizationFormatter` for numbers, dates, money, percentages, and file sizes.

4. **Translation Keys (SG)**: A source generator that reads JSON translation files at compile time and produces a static `T` class with strongly-typed `LocalizedString` properties. Missing translations produce build warnings.

5. **Humanizers**: Duration, ordinal, quantity, and relative time formatters with language-specific rules for 17+ languages.

The same "format a price for the current user" scenario:

```csharp
// I18NContext is set by middleware (from Accept-Language, query string, or user preferences)
// It flows across all await boundaries automatically

var price = Money.From(99.99m, CurrencyCode.EUR);
var formatted = price.Format(); // "99,99 €" in it-IT, "€99.99" in en-US

// Or via DI-injectable formatter
public class InvoiceService(GlobalizationFormatter formatter)
{
    public string FormatTotal(Order order) => formatter.FormatMoney(order.Total);
}
```

No thread culture manipulation. No manual `CultureInfo` passing. The context is ambient, immutable, and thread-safe.

---

## I18NContext: The Ambient Culture

`I18NContext` is the central coordination point. It stores the current culture using `AsyncLocal<I18NContext?>`, which means it flows automatically across `await` boundaries, into `Task.Run`, and through the entire async call chain.

### How it works

```
HTTP Request arrives
    |
    v
I18NContextMiddleware resolves culture
  (query string -> Accept-Language -> provider chain -> default)
    |
    v
I18NContext.SetFromConfig(resolvedConfig)
  - Sets AsyncLocal with immutable context
  - Syncs Thread.CurrentThread.CurrentUICulture
  - Syncs Thread.CurrentThread.CurrentCulture
    |
    v
Your code reads I18N.Culture, I18N.UI, I18N.Data
  - Extension methods use I18NContext.EffectiveCulture
  - LocalizedString.Value uses I18NContext.Current.CultureCode
  - Money.Format() uses I18NContext.EffectiveCulture
    |
    v
I18NContext.Clear() in middleware finally block
```

### Multi-scope cultures

The context stores three culture dimensions:

| Scope | Property | Purpose | Example |
|-------|----------|---------|---------|
| **UI** | `I18N.UI` | Display: labels, messages, user-facing content | `it-IT` |
| **Data** | `I18N.Data` | Storage: API responses, exports, database | `en-US` |
| **Custom** | `I18N.Scope["invoicing"]` | Specific needs: invoicing, reporting | `de-DE` |

```csharp
// Set different cultures for different purposes
I18NContext.SetCulture("it-IT");                         // UI culture
I18NContext.SetDataCulture(CultureCode.FromString("en-US")); // Data culture
I18NContext.SetScope("invoicing", CultureCode.FromString("de-DE"));

// Access from anywhere in the async flow
var uiCulture = I18N.UI;                     // it-IT
var dataCulture = I18N.Data;                 // en-US
var invoiceCulture = I18N.Scope["invoicing"]; // de-DE
```

When `SyncScopes` is enabled in `I18NConfig`, setting the UI culture automatically updates the Data culture to match. This is useful for simple applications that do not need separate cultures.

### Strongly-typed scopes

Instead of magic strings, define scope types that implement `ICultureScope`:

```csharp
public class InvoicingScope : ICultureScope
{
    public static string Name => "invoicing";
    public static CultureCode DefaultCulture => CultureCode.Italian;
}

// Set and get with type safety
I18N.SetScope<InvoicingScope>(CultureCode.German);
var culture = I18N.GetScope<InvoicingScope>();

// Use with LocalizedString
var title = product.Name.GetForScope<InvoicingScope>();
```

### Scoped execution

For temporary culture changes, use `WithCulture` / `WithCultureAsync`. These save the current context, execute the delegate, and restore the original context in a `finally` block:

```csharp
// Synchronous
I18NContext.WithCulture("de-DE", () =>
{
    var formatted = 1234.56m.FormatNumber(); // "1.234,56"
});
// Original culture restored here

// Async
await I18NContext.WithCultureAsync("fr-FR", async () =>
{
    await ProcessOrderAsync(); // Runs with fr-FR culture
});

// With return value
var result = I18NContext.WithCulture<string>("it-IT", () =>
{
    return T.Welcome.Value; // "Benvenuto nella nostra app!"
});
```

### Fallback behavior

If no context has been set explicitly, `I18NContext.Current` falls back to `CultureInfo.CurrentCulture` from the current thread. This means the system works out of the box in console applications and tests without any setup.

⚠️ **Which is why it is the wrong place to ask "what language when nobody said?"** `WithCultureAsync`
does not restore the thread's culture after an await — the continuation may resume on another pool
thread — so outside a scope `I18NContext.Current` is whatever the last piece of work on that thread
left behind. Measured: a letter for an applicant who named no language came out in Italian because
another test had rendered an Italian one on the same thread, and a background job is in the same
position as that test.

The configured answer is a contract of its own:

```csharp
// Pragmatic.Internationalization.Context — registered by UseI18N, scoped
public sealed class TheLetter(IConfiguredCultures cultures)
{
    private string LanguageFor(string? theirs)
        => theirs is { Length: > 0 } ? theirs : cultures.Default.Code;
}
```

`IConfiguredCultures` answers `Default` (the host's `i18n.DefaultCulture(...)`, merged across
providers exactly as a request resolves it) and `Supported`, and nothing else — the *current* culture
stays `I18NContext`'s business. It is scoped because a provider may be per-request, and it throws
rather than guessing when nothing is configured. ⚠️ `I18NConfigResolver` is deliberately **not** the
contract to inject: it carries the merge and the validation, so a module asking for it gets
`PRAG1641`.

---

## Money and CurrencyCode

### The Money type

`Money` is a value type that pairs a `decimal` amount with a `CurrencyCode`. All arithmetic enforces same-currency operations at runtime:

```csharp
var price = Money.From(99.99m, CurrencyCode.EUR);
var tax = Money.From(7.50m, CurrencyCode.EUR);

var total = price + tax;       // 107.49 EUR
var discounted = total * 0.9m; // 96.741 EUR -- multiplication does not round; call RoundToMinorUnit()

// Currency mismatch throws InvalidOperationException
var usd = Money.From(100m, CurrencyCode.USD);
// var mixed = price + usd; // InvalidOperationException!
```

Rounding respects the currency's minor units (ISO 4217):

```csharp
var price = Money.From(99.999m, CurrencyCode.USD);
var rounded = price.RoundToMinorUnit(); // 100.00 (USD has 2 minor units)

var yen = Money.From(999.5m, CurrencyCode.JPY);
var yenRounded = yen.RoundToMinorUnit(); // 1000 (JPY has 0 minor units)
```

### CurrencyCode

All 180+ ISO 4217 currencies are available as static properties, source-generated from official data:

```csharp
CurrencyCode.USD // US Dollar, symbol "$", 2 minor units
CurrencyCode.EUR // Euro, symbol "€", 2 minor units
CurrencyCode.JPY // Japanese Yen, symbol "¥", 0 minor units
```

Each currency exposes: `Code`, `Name`, `Symbol`, `MinorUnits`. Parsing and validation are provided via `CurrencyCode.FromCode()`, `TryFromCode()`, and `IsValid()`.

### Currency-aware formatting

Money formatting is culture-aware through `I18NContext`:

```csharp
I18NContext.SetCulture("en-US");
total.Format(); // "$107.49"

I18NContext.SetCulture("de-DE");
total.Format(); // "107,49 $"
```

The `I18N` helper provides a convenience factory:

```csharp
// Create money in the user's preferred currency
var price = I18N.CreateMoney(99.99m); // Uses I18N.Currency
var zero = I18N.Zero;                 // Zero in current currency
```

---

## Formatting

Formatting is available in two forms: extension methods (for simple use) and `GlobalizationFormatter` (for DI-injectable, testable formatting).

### Extension methods

Extension methods read the ambient culture from `I18NContext.EffectiveCulture`:

```csharp
// Numbers
1234.56m.FormatNumber();     // "1,234.56" (en-US) / "1.234,56" (de-DE)
1234.56m.FormatNumber(3);    // With 3 decimal places
0.156m.FormatPercent();      // "15.60%"
1_500_000L.FormatFileSize(); // "1.4 MB"

// Dates
DateTimeOffset.Now.FormatDate();     // "3/23/2026" (en-US) / "23.03.2026" (de-DE)
DateTimeOffset.Now.FormatTime();     // "3:45 PM" / "15:45"
DateTimeOffset.Now.FormatDateTime(); // "3/23/2026 3:45 PM"
```

### GlobalizationFormatter (DI-injectable)

For services that need testable formatting, inject `GlobalizationFormatter`:

```csharp
public class InvoiceService(GlobalizationFormatter formatter)
{
    public InvoiceDto Format(Invoice invoice) => new()
    {
        Total = formatter.FormatMoney(invoice.Total),
        TaxRate = formatter.FormatPercent(invoice.TaxRate),
        DueDate = formatter.FormatDate(invoice.DueDate),
        FileSize = formatter.FormatFileSize(invoice.AttachmentBytes)
    };
}
```

`GlobalizationFormatter` is registered as scoped by `AddPragmaticInternationalization()` and reads the culture from the current `I18NContext` at resolution time.

Available methods:

| Method | Description |
|--------|-------------|
| `FormatMoney(money)` | Money with currency symbol |
| `FormatMoney(amount, currency)` | Amount + currency as Money |
| `FormatNumber(decimal, decimals)` | Decimal with thousand separators |
| `FormatInteger(long)` | Integer with thousand separators |
| `FormatPercent(decimal, decimals)` | Decimal as percentage |
| `FormatFileSize(long)` | Bytes as human-readable size |
| `FormatDate(DateTimeOffset)` | Short date |
| `FormatDate(DateOnly)` | Short date |
| `FormatTime(TimeOnly)` | Short time |
| `FormatTime(DateTimeOffset)` | Short time |
| `FormatDateTime(DateTimeOffset)` | Short date + short time |
| `FormatDateTimeLong(DateTimeOffset)` | Long date + long time |

---

## Translation Keys (Source Generator)

The unified `Pragmatic.SourceGenerator` (referenced as analyzer) reads JSON translation files at compile time and produces a static `T` class with `LocalizedString` properties. Translations are embedded in the assembly -- no runtime file loading, no I/O, no startup cost.

### How it works

```
translations/en.json  +  translations/it.json
                 |
                 v
     [assembly: TranslationKeys]
                 |
                 v
         Source Generator
                 |
                 v
     Generated static T class
         with LocalizedString.From(("en", "..."), ("it", "..."))
```

At build time, the SG:
1. Finds all `AdditionalFiles` matching recognized translation folders
2. Auto-detects the folder structure (folder-per-culture, flat-with-suffix, or simple)
3. Parses JSON files and merges translations by key and culture
4. Generates a static `T` class with `LocalizedString` properties
5. Emits diagnostics for parse errors (PRAG1800), duplicate keys (PRAG1801), and missing translations (PRAG1802)

### LocalizedString vs T class

Most applications use both together. The choice depends on whether the content is static (developer-controlled) or dynamic (user-generated).

| Aspect | `LocalizedString` | `T` class |
|--------|-------------------|-----------|
| **Storage** | Database (JSON column) | Assembly (embedded at compile-time) |
| **Scope** | Per-entity instance | Application-wide |
| **Content** | Dynamic, user-generated | Fixed, developer-controlled |
| **Mutability** | Add/remove translations at runtime | Recompile to change |
| **Type Safety** | Dictionary-like access (`greeting["de"]`) | Strongly-typed properties (`T.Errors.NotFound`) |
| **Performance** | Lazy loading from DB | Zero-cost (in-memory) |
| **Fallback** | Automatic culture chain via `I18NContext` | Same chain (returns `LocalizedString`) |
| **EF Core** | Value converter to JSON column | Not applicable (static class) |

**Use `LocalizedString`** for: product names, category descriptions, CMS content, invoice notes.

**Use `T` class** for: UI labels, email subjects, system notifications — application strings.

Error and validation messages are neither: an error carries a `Code`, a validation issue a message key,
and both are resolved to text at the response boundary by the registered `IErrorMessageResolver`.

```csharp
// Hybrid usage -- both in the same handler
var product = await _repo.GetByIdAsync(Id, ct);
if (product is null)
    return NotFoundError.For("Product", Id);  // localized from its Code, not from a string

return new ProductResponse
{
    Name = product.Name.Value,           // LocalizedString (per-entity, from DB)
    Description = product.Description.Value,
    AvailabilityLabel = T.Catalog.InStock.Value   // T class (fixed application string)
};
```

### Folder structure auto-detection

The SG auto-detects three folder layouts from the first `AdditionalFile` path. No configuration needed.

**Structure A: Folder per culture** -- each culture has its own subdirectory.

```
translations/
+-- en/
|   +-- common.json
|   +-- errors.json
+-- it/
    +-- common.json
    +-- errors.json
```

**Structure B: Flat with culture suffix** -- culture code in the filename.

```
translations/
+-- common.en.json
+-- common.it.json
+-- errors.en.json
+-- errors.it.json
```

**Structure C: Simple** -- one file per culture, filename is the culture code.

```
translations/
+-- en.json
+-- it.json
```

Recognized root folders: `translations`, `i18n`, `locales`, `lang`. Files matching `*.translations.json` or `*.i18n.json` are also included.

### EmbedTranslations mode

When `EmbedTranslations = true` (default), the `T` class embeds all translation values at compile time:

```csharp
// Generated: all values baked into the assembly
public static LocalizedString Welcome => LocalizedString.From(
    ("en", "Welcome!"),
    ("it", "Benvenuto!"));
```

It also generates `TTranslations`, an `ILocalizationProvider` over the same values, which the host of any
application that includes the module registers: a lookup by key — `IStringLocalizer`, `t:` in a document
template, a localized error — finds the module's translations with no file copied beside the host. Plural
forms are not embedded.

When `EmbedTranslations = false`, the `T` class generates `LocalizationKey` references for runtime lookup:

```csharp
// Generated: key only, resolved at runtime via ILocalizationProvider
public static LocalizationKey Welcome => new("welcome");
```

This allows swapping translation sources (database, remote API) without recompiling.

---

## Plural Rules

Pragmatic implements CLDR-compliant plural rules for 40+ languages. The rules are organized by language family in partial classes: `PluralRules.Germanic.cs`, `PluralRules.Romance.cs`, `PluralRules.Slavic.cs`, `PluralRules.Other.cs`.

### Six CLDR categories

| Category | Description | Example Languages |
|----------|-------------|-------------------|
| `Zero` | Explicit zero form | Arabic, Welsh, Latvian |
| `One` | Singular form | English, German, Russian, most languages |
| `Two` | Dual form | Arabic, Welsh, Hebrew, Slovenian |
| `Few` | Paucal / small number | Russian (2-4), Polish (2-4), Czech (2-4) |
| `Many` | Large number form | Russian (5-20), Arabic (11-99) |
| `Other` | Default plural | All languages (always present as fallback) |

### Language family behavior

**Germanic** (en, de, nl, sv, da, no, fi): Simple one/other split. Count 1 is `One`, everything else is `Other`.

**Romance** (fr, it, es, pt, ca): Both 0 and 1 are `One`. Count 2+ is `Other`.

**East Slavic** (ru, uk, be): One/few/many based on last digits. `1` (not 11) is `One`. `2-4` (not 12-14) is `Few`. Everything else is `Many`.

**East Asian** (ja, zh, ko, vi, th) and **Turkish-family** (tr, az): No plural forms, always `Other`.

```csharp
PluralRules.GetCategory("en", 1);   // One
PluralRules.GetCategory("en", 5);   // Other
PluralRules.GetCategory("fr", 0);   // One (Romance: 0 is singular)
PluralRules.GetCategory("ru", 2);   // Few
PluralRules.GetCategory("ru", 5);   // Many
PluralRules.GetCategory("ar", 2);   // Two (Arabic uses all 6 categories)
PluralRules.GetCategory("ja", 1);   // Other (no plural forms)
```

### Using plurals in translations

Define plural forms in JSON using CLDR category names as keys:

```json
{
  "items": {
    "one": "1 item",
    "other": "{count} items"
  }
}
```

Resolve via `StringLocalizer`:

```csharp
var localizer = new StringLocalizer(provider, options);
localizer.Plural("items", 1); // "1 item"
localizer.Plural("items", 5); // "5 items"
```

When a specific category is not defined, `PluralString` falls back to `Other`. If `Other` is also missing, it returns an empty string.

---

## Humanizers

Humanizers format values in a way that humans read naturally. Four formatters are provided.

### DurationFormatter

Formats `TimeSpan` values with language-specific units. Three formats are available:

| Format | Example (en) | Example (de) |
|--------|-------------|-------------|
| `Short` | `2h 30m` | `2Std 30Min` |
| `Long` | `2 hours 30 minutes` | `2 Stunden 30 Minuten` |
| `Compact` | `2:30:00` | `2:30:00` |

```csharp
var formatter = new DurationFormatter("en", DurationFormat.Short);
formatter.Format(TimeSpan.FromMinutes(150)); // "2h 30m"

var longFmt = DurationFormatter.Long("en");
longFmt.Format(TimeSpan.FromHours(2.5)); // "2 hours 30 minutes"
```

Supported languages: en, de, fr, it, es, pt, ru, zh, ja, ko, ar, nl, pl, sv, no, da, fi.

### OrdinalFormatter

Formats numbers as ordinals with language-specific suffixes:

```csharp
var ordinal = new OrdinalFormatter("en");
ordinal.Format(1);  // "1st"
ordinal.Format(2);  // "2nd"
ordinal.Format(21); // "21st"

var german = OrdinalFormatter.ForCulture("de");
german.Format(1);   // "1."

var feminine = new OrdinalFormatter("fr", OrdinalGender.Feminine);
feminine.Format(1); // "1ère"
```

### QuantityFormatter

Abbreviates large numbers with language-specific suffixes:

```csharp
var qty = new QuantityFormatter("en");
qty.Format(1_500);         // "1.5K"
qty.Format(2_300_000);     // "2.3M"
qty.Format(1_200_000_000); // "1.2B"

var german = QuantityFormatter.ForCulture("de");
german.Format(1_500_000);  // "1,5 Mio"
```

### RelativeTimeFormatter

Formats time spans as human-readable relative time:

```csharp
var relTime = new RelativeTimeFormatter(localizer);
relTime.Format(TimeSpan.FromMinutes(5)); // "5 minutes ago"
relTime.Format(TimeSpan.FromDays(1));    // "yesterday"
relTime.Format(TimeSpan.FromDays(14));   // "2 weeks ago"
```

`RelativeTimeFormatter` requires an `IStringLocalizer` because the labels ("ago", "yesterday", "weeks") are themselves translatable.

---

## Localization Providers

For runtime-based localization (when `EmbedTranslations = false` or for supplemental translations), the provider system offers a composable pipeline.

### ILocalizationProvider

```csharp
public interface ILocalizationProvider
{
    IReadOnlyList<string> SupportedCultures { get; }
    int Priority => 0; // Higher = checked first
    string? GetString(string key, string culture);
    PluralString? GetPlural(string key, string culture);
    IReadOnlyDictionary<string, string> GetAll(string culture);
    IReadOnlyDictionary<string, PluralString> GetAllPlurals(string culture);
}
```

### Built-in providers

| Provider | Description | Use Case |
|----------|-------------|----------|
| `InMemoryLocalizationProvider` | Programmatic, fluent API | Development, testing |
| `JsonLocalizationProvider` | Reads JSON files at runtime | File-based translations |
| `CompositeLocalizationProvider` | Chains providers by priority | Multi-source fallback |

```csharp
var provider = new InMemoryLocalizationProvider()
    .AddString("en", "welcome", "Welcome!")
    .AddString("de", "welcome", "Willkommen!")
    .AddPlural("en", "items",
        (PluralCategory.One, "1 item"),
        (PluralCategory.Other, "{count} items"));

var composite = new CompositeLocalizationProvider(
    new[] { jsonProvider, memoryProvider });
// Checked in Priority order (descending). First non-null wins.
```

### IStringLocalizer

The main interface for string localization with interpolation and pluralization:

```csharp
var localizer = new StringLocalizer(provider, options);

var welcome = localizer["welcome"];              // Simple lookup
var greeting = localizer["hello", "John"];        // Interpolation
var items = localizer.Plural("items", 5);         // Plural
var german = localizer.WithCulture("de");         // Culture switch
```

---

## ASP.NET Core Integration

The `Pragmatic.Internationalization.AspNetCore` package provides middleware, DI registration, and JSON serialization.

### Setup

```csharp
// Program.cs
builder.Services.AddPragmaticInternationalization(options =>
{
    options.DefaultUICulture = CultureCode.EnglishUS;
    options.SupportedCultures = [CultureCode.English, CultureCode.Italian, CultureCode.German];
    options.SyncScopes = true;
});

var app = builder.Build();
app.UsePragmaticInternationalization();
```

### Culture resolution order

The middleware resolves culture in this order:

1. **Query string**: `?culture=de-DE`
2. **Accept-Language header**: standard HTTP header
3. **Provider chain**: `SystemConfigProvider` (from `appsettings.json`, priority 0), custom providers at higher priorities
4. **Default culture**: from `I18NOptions.DefaultUICulture`

### Provider chain

The config provider system (`II18NConfigProvider`) allows layered culture resolution. Only
`SystemConfigProvider` ships in the box; the others below are **yours to implement** (derive
from `CachedConfigProvider` for TTL caching) following the suggested priority convention:

| Provider | Priority | Source | Shipped |
|----------|----------|--------|---------|
| request-level (example) | 300 | query string, Accept-Language | handled by the middleware |
| user-level (example) | 200 | user preferences from database | you implement it |
| tenant-level (example) | 100 | tenant settings | you implement it |
| `SystemConfigProvider` | 0 | `I18NOptions` / `appsettings.json` | ✅ built-in |

Register additional providers via the builder:

```csharp
builder.Services.AddPragmaticInternationalization(options => { ... })
    .AddConfigProvider<TenantConfigProvider>()
    .AddConfigProvider<UserConfigProvider>();
```

### JSON serialization

`Money` and `CurrencyCode` types serialize automatically when `AddPragmaticInternationalization()` is called:

```json
{
  "price": {
    "amount": 99.99,
    "currency": "EUR"
  }
}
```

JSON converters are registered for: `Money`, `CurrencyCode`, `CultureCode`, `LanguageCode`, `CountryCode`, `LocalizedString` (plus nullable variants).

For the full ASP.NET Core surface (fluent `I18NBuilder`, `UseI18N` in a Pragmatic host, translation endpoint, localized ProblemDetails), see [Integration](integration.md).

---

## EF Core Integration

The `Pragmatic.Internationalization.EFCore` package provides value converters for database storage.

```csharp
// In DbContext.OnModelCreating
modelBuilder.ApplyPragmaticInternationalization();

// Or via conventions
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
{
    builder.ApplyPragmaticInternationalizationConventions();
}
```

Value converters applied automatically:
- `CurrencyCode` maps to `varchar(3)`
- `LocalizedString` maps to a JSON column

`Money` has **no automatic converter** -- map it manually as two columns (amount + currency);
`MoneyConfiguration` provides the recommended precision/scale constants. See [Integration](integration.md#ef-core).

---

## Ecosystem Integration

Pragmatic.Internationalization integrates with other Pragmatic modules through the ambient `I18NContext`.

### Persistence

Entities with `LocalizedString` properties store translations as JSON columns via EF Core value converters. The `Money` type is stored as two columns (amount + currency code) via manual mapping.

### Endpoints

Endpoint handlers can access formatted values via extension methods or inject `GlobalizationFormatter`. The middleware sets `I18NContext` before any endpoint handler runs.

### Validation

`Pragmatic.Validation` provides three rules for these types: `[NonNegativeMoney]`, `[PositiveMoney]` and `[SupportedCurrency]`. They validate `Money` and `CurrencyCode` properties, and the source generator emits their check like any other Pragmatic validation rule.

### Composition

When `Pragmatic.Composition` is referenced, the SG emits assembly-level `[PragmaticMetadata]` attributes containing translation metadata (class name, namespace, key count, cultures, files). This enables host applications to discover translation metadata from referenced assemblies.

---

## Testing

### TestI18N and TestI18NScope

The `Testing` namespace provides helpers for isolated test execution:

```csharp
// Static helper -- sets culture, runs action, restores previous context
TestI18N.WithCulture(CultureCode.Italian, () =>
{
    var formatted = price.Format();
    formatted.Should().Contain(",");
});

// Disposable scope -- for more control
using var scope = new TestI18NScope(CultureCode.German);
var result = 1234.56m.FormatNumber(); // "1.234,56"
// Context restored when scope is disposed

// Separate UI and Data cultures
TestI18N.WithCultures(
    uiCulture: CultureCode.Italian,
    dataCulture: CultureCode.English,
    () =>
    {
        I18N.UI.Code.Should().Be("it");
        I18N.Data.Code.Should().Be("en");
    });
```

### Manual capture/restore

For frameworks that do not support `using` scopes easily:

```csharp
var snapshot = I18NContext.Capture();
try
{
    I18NContext.SetCulture("de-DE");
    // ... test code ...
}
finally
{
    I18NContext.Restore(snapshot);
}
```

---

## See Also

- [Getting Started](getting-started.md) -- Install and configure in 5 minutes
- [Integration](integration.md) -- ASP.NET Core, `I18NBuilder`, translation endpoint, localized ProblemDetails, EF Core
- [Translation Keys](translation-keys.md) -- `[TranslationKeys]`, SG-generated constants, folder structures
- [Common Mistakes](common-mistakes.md) -- Wrong/right patterns with explanations
- [Troubleshooting](troubleshooting.md) -- Diagnostics reference, FAQ, problem/solution guide
