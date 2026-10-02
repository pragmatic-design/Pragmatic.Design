---
title: "Pragmatic.Internationalization"
description: "Comprehensive internationalization (i18n) and localization (l10n) for .NET 10: async-safe culture"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Internationalization/README.md
sidebar:
  order: 0
  label: Overview
---
Comprehensive internationalization (i18n) and localization (l10n) for .NET 10: async-safe culture
context, money & currency value types, culture-aware formatting, compile-time-checked translations with
plural support, and humanizers.

## The Problem

.NET gives you low-level globalization primitives (`CultureInfo`, `.resx`), but a real multilingual app
hits the same gaps:

- **Thread culture is fragile**: `CurrentCulture` is thread-local; after an `await` your continuation
  may run on a different thread, and `DefaultThreadCurrentCulture` is a process-wide, multi-tenant trap.
- **No multi-scope culture**: a request may need Italian UI, English API responses, German invoices.
  .NET gives you two slots; beyond that you're on your own.
- **Money is not a type**: every app reinvents a `Money` struct, silently adds USD to EUR, and formats
  inconsistently.
- **Plurals are language-specific**: English has 2 forms, Russian 3, Arabic 6; `count == 1 ? a : b`
  is wrong for most of the world.
- **`.resx` doesn't compose**: translations scatter across XML with no build-time completeness check;
  you can ship with half a language missing.

## The Solution: five pillars

1. **`I18NContext`**: an `AsyncLocal` ambient context that flows across `await`, supports multiple
   culture scopes (UI, Data, custom), and syncs with .NET thread cultures.
2. **`Money` & `CurrencyCode`**: value types enforcing same-currency arithmetic, ISO 4217 metadata,
   and culture-aware formatting.
3. **Formatting**: extension methods + an injectable `GlobalizationFormatter` for numbers, dates,
   money, percentages, and file sizes.
4. **Translation keys (SG)**: a generator reads JSON at compile time and produces a static `T` class
   of strongly-typed `LocalizedString` properties; missing translations are build warnings (PRAG1802).
5. **Humanizers**: duration, ordinal, quantity, and relative-time formatters for 17+ languages.

```csharp
var price = Money.From(99.99m, CurrencyCode.EUR);
price.Format();                                  // "99,99 €" (it-IT) / "€99.99" (en-US)

var subject = T.Emails.OrderShipped.Value;       // compile-time-safe translation, current culture
```

The middleware sets `I18NContext` from `Accept-Language` / query string / a provider chain, and it
flows across every `await` automatically.

## Quick Start: translations

1. Add JSON files under `translations/` (one per culture: `en.json`, `it.json`, …).
2. Include them in your `.csproj`: `<AdditionalFiles Include="translations/*.json" />`.
3. Reference `Pragmatic.SourceGenerator`.
4. Use the generated `T` class:

```csharp
var text    = T.Welcome.Value;            // current culture
var italian = T.Common.Welcome["it"];     // a specific culture
```

Full walkthrough: [Getting Started](/modules/i18n/getting-started/).

## Packages

| Package | Description |
|---------|-------------|
| `Pragmatic.Internationalization` | Core: context, Money/CurrencyCode, formatting, humanizers, generated `T` |
| `Pragmatic.Internationalization.AspNetCore` | Culture middleware, ProblemDetails localization, frontend endpoint |
| `Pragmatic.Internationalization.EFCore` | `LocalizedString` (JSON column) and `CurrencyCode` (varchar(3)) entity storage |

## Installation

```bash
dotnet add package Pragmatic.Internationalization
dotnet add package Pragmatic.SourceGenerator   # generates the strongly-typed T class
```

## Features at a glance

- Async-safe, multi-scope culture context (UI / Data / custom).
- `Money` with same-currency-enforced arithmetic + ISO 4217 `CurrencyCode`.
- Culture-aware formatters for numbers, dates, money, percent, file sizes.
- Compile-time-checked translations with CLDR plural rules.
- Humanizers for duration, ordinals, quantities, relative time (17+ languages).
- ASP.NET Core middleware + EF Core `LocalizedString` storage.

## Status

**Functional** within 1.0.0-alpha: the five pillars, the translation generator, and the ASP.NET Core and
EF Core integrations; the surface may still change before 1.0. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md) and
the [CHANGELOG](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Internationalization/CHANGELOG.md).

| [Concepts](/modules/i18n/concepts/) | The five pillars in depth: culture context, Money/Currency, formatting, plural rules, humanizers, integrations |
| [Getting Started](/modules/i18n/getting-started/) | Translations, Money, formatting, and humanizers from zero |
| [Integration](/modules/i18n/integration/) | ASP.NET Core middleware, `I18NBuilder`/`UseI18N`, translation endpoint, localized ProblemDetails, EF Core |
| [Translation Keys](/modules/i18n/translation-keys/) | JSON layout, the generated `T` class, plural forms, completeness warnings |
| [Common Mistakes](/modules/i18n/common-mistakes/) | The most frequent i18n pitfalls |
| [Troubleshooting](/modules/i18n/troubleshooting/) | Problem/solution guide with diagnostics |

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer (for the translation `T` class)

## License

Part of the [Pragmatic.Design](/modules/i18n/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Internationalization is **MIT-licensed**.
