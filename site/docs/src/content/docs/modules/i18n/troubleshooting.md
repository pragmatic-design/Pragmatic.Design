---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Internationalization. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Internationalization/docs/troubleshooting.md
sidebar:
  order: 6
---
Practical problem/solution guide for Pragmatic.Internationalization. Each section covers a common issue, the likely causes, and the fix.

---

## Formatting Uses Wrong Culture

Numbers, dates, or money display in the server's default locale instead of the user's preferred culture.

### Checklist

1. **Did you call `UsePragmaticInternationalization()` in Program.cs?** The middleware sets `I18NContext` from the request. Without it, formatting falls back to `CultureInfo.CurrentCulture`.

   ```csharp
   app.UsePragmaticInternationalization(); // Before UseRouting()
   ```

2. **Is the middleware registered early enough?** It must run before any code that reads `I18NContext`. Place it before `UseRouting()` and `MapPragmaticEndpoints()`.

3. **Is the culture in the supported list?** If the requested culture is not in `I18NOptions.SupportedCultures`, the resolver falls back to the default. Check that the culture is listed:

   ```csharp
   options.SupportedCultures = [CultureCode.English, CultureCode.Italian, CultureCode.German];
   ```

4. **Is the client sending the culture?** Check the request for:
   - Query string: `?culture=de-DE`
   - `Accept-Language` header: `Accept-Language: de-DE,de;q=0.9,en;q=0.8`

5. **For console or background services:** Set the culture manually since there is no HTTP middleware:

   ```csharp
   I18NContext.SetCulture("de-DE");
   ```

---

## T Class Has No Properties

The `[TranslationKeys]` attribute is present but the generated `T` class is empty or does not exist.

### Checklist

1. **Are translation files listed as `AdditionalFiles`?** The SG only sees files explicitly included with this item type:

   ```xml
   <ItemGroup>
     <AdditionalFiles Include="translations/*.json" />
   </ItemGroup>
   ```

   Files included as `Content`, `None`, or `EmbeddedResource` are invisible to the source generator.

2. **Are the files in a recognized folder?** The SG recognizes: `translations`, `i18n`, `locales`, `lang`. Files matching `*.translations.json` or `*.i18n.json` are also included regardless of folder name.

3. **Is the JSON valid?** Diagnostic PRAG1800 fires when a translation file cannot be parsed. Check for syntax errors (trailing commas, missing quotes, BOM characters).

4. **Do the files contain string values?** Diagnostic PRAG1803 fires when a file parses successfully but contains no string leaf values. Nested objects without string leaves are skipped:

   ```json
   // This file triggers PRAG1803 -- no string values
   {
     "section": {
       "subsection": {}
     }
   }
   ```

5. **Is the source generator referenced?** In your `.csproj`, the SG must be referenced as an analyzer:

   ```xml
   <PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.*">
     <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
   </PackageReference>
   ```

---

## Missing Translation Warning (PRAG1802)

The build shows `PRAG1802: Translation key 'X' exists in 'en' but is missing in 'it'`.

### What it means

A key is defined in the default culture file but not in another culture file. At runtime, `LocalizedString.Value` will fall back to the default culture for that key.

### Fix

Add the missing key to the incomplete translation file. If the fallback behavior is intentional (e.g., the key is English-only by design), you can suppress the warning:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);PRAG1802</NoWarn>
</PropertyGroup>
```

Or suppress it for a specific file in `.editorconfig`:

```ini
[translations/it.json]
dotnet_diagnostic.PRAG1802.severity = none
```

---

## Duplicate Key Warning (PRAG1801)

The build shows `PRAG1801: Translation key 'welcome' is defined in multiple files for culture 'en'`.

### What it means

The same key appears in two or more JSON files for the same culture. The SG uses the last definition it encounters. This is usually unintentional -- you may have copied a file and forgotten to remove duplicate keys.

### Fix

Consolidate the duplicate key into a single file. If you use the folder-per-culture structure, each file (e.g., `common.json` vs `errors.json`) creates a separate namespace, so keys only conflict when they appear in the same logical file.

---

## Money Formatting Shows Wrong Symbol or Position

`Money.Format()` shows the currency symbol in the wrong position (e.g., `$107.49` instead of `107,49 EUR`).

### Possible causes

1. **Wrong culture context.** The symbol position and decimal separator come from the culture, not the currency:

   ```csharp
   I18NContext.SetCulture("en-US");
   money.Format(); // "$107.49" -- symbol before, dot separator

   I18NContext.SetCulture("de-DE");
   money.Format(); // "107,49 $" -- symbol after, comma separator
   ```

2. **No `I18NContext` set.** Without middleware, formatting uses `CultureInfo.CurrentCulture`. See "Formatting Uses Wrong Culture" above.

3. **Using `Amount.ToString("C")` directly instead of `money.Format()`.** The `ToString("C")` method formats using the culture's default currency, not the `Money.Currency`. A EUR amount formatted with `en-US` culture via `ToString("C")` produces `$107.49` (USD symbol), not `EUR 107.49`.

---

## LocalizedString.Value Returns Wrong Language

`product.Name.Value` returns the English text instead of the user's language.

### Checklist

1. **Is `I18NContext` set?** The `Value` property reads `I18NContext.Current.CultureCode`. If no context is set, it falls back to `CultureInfo.CurrentCulture`.

2. **Does the `LocalizedString` contain the requested culture?** Check `product.Name.Cultures` to see which cultures are stored. The fallback chain is: exact match (`de-AT`) -> base language (`de`) -> default culture (`en`).

3. **Is the culture code format correct?** `LocalizedString` uses the culture code string for lookup. If you stored `"de"` but the context is `"de-DE"`, the fallback chain will try `"de-DE"` first (miss), then `"de"` (match). This is correct behavior. But if you stored `"de-DE"` and the context is `"de"`, the exact match fails and it falls to `"en"`.

4. **For Data vs UI culture:** Use `.UIValue` for display and `.DataValue` for storage/API output:

   ```csharp
   // UI culture: it-IT, Data culture: en-US
   product.Name.UIValue;   // Italian text
   product.Name.DataValue; // English text
   product.Name.Value;     // Italian text (same as UIValue, uses CultureCode)
   ```

---

## EF Core: CurrencyCode or Money Column Errors

Database migration or query errors related to `CurrencyCode` or `Money` columns.

### Checklist

1. **Did you call `ApplyPragmaticInternationalization()` in OnModelCreating?**

   ```csharp
   protected override void OnModelCreating(ModelBuilder modelBuilder)
   {
       modelBuilder.ApplyPragmaticInternationalization();
   }
   ```

2. **Or the convention-based alternative?**

   ```csharp
   protected override void ConfigureConventions(ModelConfigurationBuilder builder)
   {
       builder.ApplyPragmaticInternationalizationConventions();
   }
   ```

3. **Is the `Pragmatic.Internationalization.EFCore` package referenced?** The value converters live in this separate package.

4. **Column type for CurrencyCode:** Maps to `varchar(3)`. If your existing column is a different type, you need a migration to change it.

---

## JSON Serialization Not Working

`Money` or `CurrencyCode` values serialize as empty objects or throw serialization errors.

### Checklist

1. **Did you call `AddPragmaticInternationalization()`?** This registers JSON converters for `Money`, `CurrencyCode`, `CultureCode`, `LanguageCode`, and `CountryCode`:

   ```csharp
   builder.Services.AddPragmaticInternationalization();
   ```

2. **Are you using a custom `JsonSerializerOptions` instance?** The converters are registered on the HTTP JSON options. If you create a separate `JsonSerializerOptions` for manual serialization, add the converters manually:

   ```csharp
   var options = new JsonSerializerOptions();
   options.AddPragmaticInternationalization();
   ```

3. **Expected JSON format for Money:**

   ```json
   { "amount": 99.99, "currency": "EUR" }
   ```

   If you see `{ "amount": 0, "currency": null }`, the converter is not registered.

---

## Diagnostics Reference

| ID | Severity | Description | Fix |
|----|----------|-------------|-----|
| PRAG1800 | Error | Translation JSON file could not be parsed | Fix JSON syntax errors (trailing commas, missing quotes, BOM) |
| PRAG1801 | Warning | Duplicate translation key for same culture | Consolidate duplicate keys into a single file per culture |
| PRAG1802 | Warning | Translation key exists in default culture but missing in another | Add the missing key, or suppress if fallback is intentional |
| PRAG1803 | Info | Translation file contains no valid translation keys | Add string key-value pairs, or remove the empty file |

### Viewing diagnostics

- **Visual Studio**: Error List window, filter by "PRAG18"
- **CLI**: `dotnet build` output includes diagnostic messages with file and line information
- **Rider**: Problems tool window

---

## FAQ

### What happens when a translation key is missing?

`LocalizedString.Value` uses a fallback chain: exact culture (`de-AT`) -> base language (`de`) -> default culture (typically `en`). If no translation exists in any fallback, it returns an empty string.

For `T` class keys, missing translations are caught at build time by PRAG1802. At runtime, the key always has the embedded values from all cultures present at compile time.

### Can I use both `LocalizedString` and `T` class in the same project?

Yes. This is the recommended approach. Use `T` class for static application strings (UI labels, email subjects — error messages are resolved from the error's `Code` instead) and `LocalizedString` for dynamic per-entity content (product names, descriptions).

### How do I add a new language?

For the `T` class: add a new JSON file for the culture (e.g., `translations/fr.json`), populate all keys, and rebuild. PRAG1802 warnings will flag any missing keys.

For `LocalizedString`: add translations at runtime via the entity's API (`product.Name.Set("fr", "Widget")`).

### Does I18NContext work with background services?

Yes, but you must set the culture manually since there is no HTTP middleware:

```csharp
I18NContext.SetCulture("en-US");
await ProcessBackgroundJobAsync();
I18NContext.Clear(); // Clean up when done
```

### Can I override the query string parameter name?

Yes -- set `I18NOptions.QueryStringKey` (default `"culture"`):

```csharp
builder.Services.AddPragmaticInternationalization(options =>
{
    options.QueryStringKey = "lang";  // ?lang=de-DE
});
```

### How do plurals work with the T class?

The `T` class does not generate `PluralString` members -- a nested JSON object becomes a nested
group of `LocalizedString` properties (`"items": { "one": ..., "other": ... }` generates
`T.Items.One` / `T.Items.Other`).

For CLDR-correct plural selection, use the runtime provider system: define plural forms in a
runtime provider (`JsonLocalizationProvider` / `InMemoryLocalizationProvider.AddPlural`) and
resolve them with `StringLocalizer.Plural("items", count)`, which picks the right category via
`PluralRules.GetCategory`.

### Does the system support RTL languages?

The formatting system uses .NET's `CultureInfo` which handles RTL number formatting (Arabic, Hebrew). The translation system stores and retrieves strings without directional assumptions. RTL layout is a UI concern handled by your frontend framework.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working i18n usage across entities and endpoints.
- **Getting Started**: See [getting-started.md](/modules/i18n/getting-started/) for initial setup.
- **Concepts**: See [concepts.md](/modules/i18n/concepts/) for architecture and design decisions.
- **Translation Keys**: See [translation-keys.md](/modules/i18n/translation-keys/) for SG configuration and folder structures.
