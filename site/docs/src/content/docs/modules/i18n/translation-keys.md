---
title: "Translation Keys"
description: "The unified `Pragmatic.SourceGenerator` creates a static `T` class with `LocalizedString` properties from JSON translation files. Translations are embedded at c"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Internationalization/docs/translation-keys.md
sidebar:
  order: 4
---
The unified `Pragmatic.SourceGenerator` creates a static `T` class with `LocalizedString` properties from JSON translation files. Translations are embedded at compile-time -- no runtime file loading required.

## Setup

### 1. Add Translation Files

Create JSON translation files in your project:

```
translations/
+-- en.json
+-- it.json
```

```json
// en.json
{
  "welcome": "Welcome to our app!",
  "errors": {
    "notFound": "Resource not found"
  }
}

// it.json
{
  "welcome": "Benvenuto nella nostra app!",
  "errors": {
    "notFound": "Risorsa non trovata"
  }
}
```

### 2. Include as AdditionalFiles

In your `.csproj`:

```xml
<ItemGroup>
  <AdditionalFiles Include="translations/*.json" />
</ItemGroup>
```

### 3. Reference the Source Generator

```xml
<ItemGroup>
  <PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.*">
    <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

### 4. Add the Assembly Attribute

In `AssemblyAttributes.cs` (or any file):

```csharp
using Pragmatic.Internationalization.Attributes;

[assembly: TranslationKeys]
```

## Generated T Class

From the JSON files above, the generator produces:

```csharp
public static class T
{
    public static LocalizedString Welcome => LocalizedString.From(
        ("en", "Welcome to our app!"),
        ("it", "Benvenuto nella nostra app!"));

    public static class Errors
    {
        public static LocalizedString NotFound => LocalizedString.From(
            ("en", "Resource not found"),
            ("it", "Risorsa non trovata"));
    }
}
```

## Usage

```csharp
// Current culture access (via I18NContext)
I18NContext.SetCulture("en");
Console.WriteLine(T.Welcome.Value);  // "Welcome to our app!"

I18NContext.SetCulture("it");
Console.WriteLine(T.Welcome.Value);  // "Benvenuto nella nostra app!"

// Direct culture access via indexer
Console.WriteLine(T.Welcome["en"]);  // "Welcome to our app!"
Console.WriteLine(T.Welcome["it"]);  // "Benvenuto nella nostra app!"

// Implicit string conversion (uses current culture)
string message = T.Welcome;

// LocalizedString properties
var cultures = T.Welcome.Cultures;  // ["en", "it"]
var count = T.Welcome.Count;        // 2
var isEmpty = T.Welcome.IsEmpty;    // false
```

## TranslationKeysAttribute Configuration

```csharp
[assembly: TranslationKeys(
    ClassName = "T",           // Default: "T"
    Namespace = "",            // Default: auto-derived from folder
    EmbedTranslations = true,  // Default: true (LocalizedString with values)
    ByFile = true,             // Default: true (nested classes per file)
    DefaultCulture = "en")]    // Default: "en"
```

| Property | Default | Description |
|----------|---------|-------------|
| `ClassName` | `"T"` | Root class name for generated keys |
| `Namespace` | `""` | Namespace (auto-derived if empty) |
| `EmbedTranslations` | `true` | `true`: `LocalizedString.From(...)` with values. `false`: `LocalizationKey` for runtime lookup |
| `ByFile` | `true` | `true`: nested classes per file. `false`: all keys at root level |
| `DefaultCulture` | `"en"` | Default culture for ordering translations |

### EmbedTranslations = false

When `EmbedTranslations = false`, the `T` class generates `LocalizationKey` references instead of `LocalizedString`. These are lightweight key strings resolved at runtime through an `ILocalizationProvider`. This allows swapping translation sources without recompiling.

## The Keys as Constants

The members of `T` are properties, and an attribute argument can only be a constant. So beside `T` the generator writes `TKeys` (`{ClassName}Keys`): the same hierarchy of static classes, each key a `const string` holding its full key, in both modes.

```csharp
public static class TKeys
{
    /// <summary>Key: "welcome"</summary>
    public const string Welcome = "welcome";

    public static class Errors
    {
        /// <summary>Key: "errors.notFound"</summary>
        public const string NotFound = "errors.notFound";
    }
}
```

Use it where a property cannot go — a validation rule's `MessageKey` above all:

```csharp
[GreaterThanOrEqualProperty(nameof(From), MessageKey = TKeys.Validation.LeaveRequest.EndsBeforeItStarts)]
public required DateOnly To { get; init; }
```

Written as a string, the key compiles whatever the JSON says, and renaming it there leaves the rule pointing at nothing. Through `TKeys` the rename is a compile error.

## Folder Structures

The source generator auto-detects three folder layouts from the first `AdditionalFile` path. No configuration needed.

Detection order:
1. Is the parent directory a culture code? --> **Folder per culture**
2. Does the filename contain a dot where the last segment is a culture code? --> **Flat with suffix**
3. Otherwise --> **Simple** (filename is the culture code)

### Structure A: Folder per Culture

```
translations/
+-- en/
|   +-- common.json
|   +-- errors.json
+-- it/
    +-- common.json
    +-- errors.json
```

```xml
<AdditionalFiles Include="translations/**/*.json" />
```

Each culture has its own subdirectory. Files with the same name across cultures are matched together. Each file becomes a nested class: `T.Common.Welcome`, `T.Errors.NotFound`.

### Structure B: Flat with Culture Suffix

```
translations/
+-- common.en.json
+-- common.it.json
+-- errors.en.json
+-- errors.it.json
```

```xml
<AdditionalFiles Include="translations/*.json" />
```

All files in one folder. The culture code is the last dot-segment before `.json`. Same result: `T.Common.Welcome`, `T.Errors.NotFound`.

### Structure C: Simple (One File per Culture)

```
translations/
+-- en.json
+-- it.json
```

```xml
<AdditionalFiles Include="translations/*.json" />
```

One file per culture. Simplest layout. Dotted JSON keys create nested classes: `"errors.notFound"` --> `T.Errors.NotFound`.

### Recognized Root Folders

Files are picked up under these directory names: `translations`, `i18n`, `locales`, `lang`. Files matching `*.translations.json` or `*.i18n.json` are also included regardless of folder.

## Localization Providers

For runtime-based localization (when `EmbedTranslations = false` or for supplemental translations):

### ILocalizationProvider

```csharp
public interface ILocalizationProvider
{
    IReadOnlyList<string> SupportedCultures { get; }
    int Priority => 0;  // Higher = checked first
    string? GetString(string key, string culture);
    PluralString? GetPlural(string key, string culture);
    IReadOnlyDictionary<string, string> GetAll(string culture);
    IReadOnlyDictionary<string, PluralString> GetAllPlurals(string culture);
}
```

### Built-In Providers

**InMemoryLocalizationProvider** -- programmatic, for development and testing:

```csharp
var provider = new InMemoryLocalizationProvider()
    .AddString("en", "welcome", "Welcome!")
    .AddString("de", "welcome", "Willkommen!")
    .AddPlural("en", "items",
        (PluralCategory.One, "1 item"),
        (PluralCategory.Other, "{count} items"));
```

**JsonLocalizationProvider** -- reads from JSON files at runtime:

```csharp
var jsonProvider = new JsonLocalizationProvider("Resources/Translations", options);
```

**CompositeLocalizationProvider** -- chains multiple providers with priority-based fallback:

```csharp
var composite = new CompositeLocalizationProvider(
    new[] { jsonProvider, memoryProvider });
```

Providers are checked in order of `Priority` (descending). The first provider to return a non-null value wins.

### IStringLocalizer

The main interface for string localization with interpolation and pluralization:

```csharp
var localizer = new StringLocalizer(provider, options);

var welcome = localizer["welcome"];               // Simple lookup
var greeting = localizer["hello", "John"];         // Interpolation
var items = localizer.Plural("items", 5);          // Plural
var german = localizer.WithCulture("de");          // Culture switch
```

## LocalizedString vs T Class

| Aspect | `LocalizedString` | `T` Class |
|--------|-------------------|-----------|
| Storage | Database (JSON column) | Assembly (embedded at compile-time) |
| Scope | Per-entity instance | Application-wide |
| Content | Dynamic, user-generated | Fixed, developer-controlled |
| Mutability | Add/remove translations at runtime | Recompile to change |
| Performance | Lazy loading from DB | Zero-cost (in-memory) |

**Use `LocalizedString`** for database-driven per-entity content: product names, descriptions, CMS content.

**Use `T` class** for compile-time safe application strings: UI labels, email subjects, notifications.
Error and validation messages are resolved from the error's `Code` or the issue's message key by the
`IErrorMessageResolver` — an error carries no text.

Most applications use both together:

```csharp
// T class for fixed application strings
var label = T.Catalog.InStock.Value;

// LocalizedString for dynamic entity data
var productName = product.Name.Value;
```

## Plural Rules

CLDR-compliant plural rules for 40+ languages. Six categories: `Zero`, `One`, `Two`, `Few`, `Many`, `Other`.

```csharp
PluralRules.GetCategory("en", 1);   // One
PluralRules.GetCategory("en", 5);   // Other
PluralRules.GetCategory("ru", 2);   // Few
PluralRules.GetCategory("ar", 0);   // Zero
```

Plural rules are organized by language family: Germanic, Romance, Slavic, Other. The method extracts the base language code (e.g., `"de-AT"` --> `"de"`) before applying rules.

Define plural forms in JSON:

```json
{
  "items": {
    "one": "1 item",
    "other": "{count} items"
  }
}
```

When a specific category is not defined, `PluralString` falls back to `Other`. If `Other` is also missing, it returns an empty string.

## Metadata for Cross-Assembly Discovery

When `Pragmatic.Composition` is referenced, the generator emits assembly metadata:

```csharp
[assembly: PragmaticMetadata(MetadataCategory.Translations, "1.0.0", """
{
  "className": "T",
  "namespace": "MyApp",
  "totalKeys": 25,
  "cultures": ["en", "it", "de"],
  "files": ["common", "errors", "validation"]
}
""")]
```

This enables host applications to discover translation metadata from referenced assemblies.

## Diagnostics

| ID | Severity | Description |
|------|----------|-------------|
| PRAG1800 | Error | Translation JSON file could not be parsed |
| PRAG1801 | Warning | Duplicate translation key detected for same culture |
| PRAG1802 | Warning | Translation key exists in default culture but missing in another |
| PRAG1803 | Info | Translation file contains no valid translation keys |
