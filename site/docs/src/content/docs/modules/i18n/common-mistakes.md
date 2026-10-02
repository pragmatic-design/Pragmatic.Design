---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Internationalization. Each section shows the wrong approach, the correct approach, an"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Internationalization/docs/common-mistakes.md
sidebar:
  order: 5
---
These are the most common issues developers encounter when using Pragmatic.Internationalization. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Not Setting Culture Before Formatting

**Wrong:**

```csharp
public class OrderService
{
    public string FormatTotal(Order order)
    {
        // No I18NContext set -- relies on Thread.CurrentCulture
        return order.Total.Format();
    }
}
```

**Runtime result:** The formatting uses whatever `CultureInfo.CurrentCulture` happens to be on the current thread. In a web application, this is typically the server's OS culture (often `en-US`), not the user's preferred culture. The result is inconsistent -- sometimes correct, sometimes wrong, depending on which thread handles the request.

**Right:**

```csharp
// Option A: Middleware sets it automatically (recommended)
// Program.cs
builder.Services.AddPragmaticInternationalization(options =>
{
    options.DefaultUICulture = CultureCode.EnglishUS;
    options.SupportedCultures = [CultureCode.English, CultureCode.Italian, CultureCode.German];
});
app.UsePragmaticInternationalization();

// Option B: Set manually when middleware is not available
I18NContext.SetCulture("de-DE");
return order.Total.Format(); // "107,49 €"
```

**Why:** `I18NContext` is `AsyncLocal`-based and flows across `await` boundaries. The middleware reads culture from the request (query string, `Accept-Language` header, provider chain) and sets it once per request. All downstream formatting methods read from this context automatically. Without it, formatting falls back to the thread's `CurrentCulture`, which is unpredictable in async web applications.

---

## 2. Mixing Currencies in Arithmetic

**Wrong:**

```csharp
var orderTotal = Money.From(100m, CurrencyCode.USD);
var shippingFee = Money.From(15m, CurrencyCode.EUR);

var grandTotal = orderTotal + shippingFee; // InvalidOperationException!
```

**Runtime result:** `InvalidOperationException` at runtime. The `Money` type enforces same-currency arithmetic to prevent silent bugs where you accidentally add dollars to euros.

**Right:**

```csharp
// Option A: Ensure same currency at the data boundary
var orderTotal = Money.From(100m, CurrencyCode.USD);
var shippingFee = Money.From(15m, CurrencyCode.USD); // Same currency
var grandTotal = orderTotal + shippingFee; // $115.00

// Option B: Convert first (use your exchange rate service)
var convertedFee = exchangeService.Convert(shippingFee, CurrencyCode.USD);
var grandTotal = orderTotal + convertedFee;
```

**Why:** Currency conversion is a business decision with rate sources, rounding rules, and audit trails. The `Money` type deliberately does not support implicit conversion. If you need cross-currency arithmetic, convert explicitly through your exchange rate service first, then add the results in the same currency.

---

## 3. Hardcoding Plural Forms

**Wrong:**

```csharp
public string FormatItemCount(int count)
{
    return count == 1 ? "1 item" : $"{count} items";
}
```

**Runtime result:** Works for English. Breaks for Russian (which has three plural forms: 1 file, 2 files, 5 files), French (which treats 0 as singular), and Arabic (which uses all six CLDR categories).

**Right:**

```csharp
// Option A: Via StringLocalizer with plural rules
var localizer = new StringLocalizer(provider, options);
var message = localizer.Plural("items", count); // "1 item" / "5 items" / "2 файла"

// Option B: Via PluralString directly
var plural = new PluralString(
    (PluralCategory.One, "1 item"),
    (PluralCategory.Other, "{count} items"));
var message = plural.Format(count);

// For Russian, define all needed categories:
provider.AddPlural("ru", "files",
    (PluralCategory.One, "{count} файл"),
    (PluralCategory.Few, "{count} файла"),
    (PluralCategory.Many, "{count} файлов"));
```

**Why:** CLDR defines six plural categories (`Zero`, `One`, `Two`, `Few`, `Many`, `Other`) with language-specific rules. `PluralRules.GetCategory()` determines the correct category for any count in any language. Hardcoding `count == 1` only works for Germanic languages and produces wrong plurals for half the world's languages.

---

## 4. Using CultureInfo.CurrentCulture Directly Instead of I18NContext

**Wrong:**

```csharp
public string FormatDate(DateTimeOffset date)
{
    return date.ToString("d", CultureInfo.CurrentCulture);
}

public string FormatMoney(Money money)
{
    return money.Amount.ToString("C", CultureInfo.CurrentUICulture);
}
```

**Runtime result:** Works inconsistently. `CultureInfo.CurrentCulture` is thread-local and does not flow reliably across `await` boundaries in all scenarios. It also ignores the multi-scope culture model (UI vs Data vs custom scopes).

**Right:**

```csharp
// Option A: Use extension methods (read I18NContext automatically)
public string FormatDate(DateTimeOffset date) => date.FormatDate();
public string FormatMoney(Money money) => money.Format();

// Option B: Use GlobalizationFormatter (DI-injectable)
public class OrderService(GlobalizationFormatter formatter)
{
    public string FormatDate(DateTimeOffset date) => formatter.FormatDate(date);
    public string FormatMoney(Money money) => formatter.FormatMoney(money);
}

// Option C: Access I18NContext explicitly when needed
var culture = I18NContext.Current.Culture; // CultureInfo from the ambient context
return date.ToString("d", culture);
```

**Why:** `I18NContext` is stored in `AsyncLocal`, which flows correctly across all `await` boundaries. When you set the culture via `I18NContext.SetCulture()`, it also syncs with `Thread.CurrentThread.CurrentCulture` and `CurrentUICulture` for third-party library compatibility. But reading from `CultureInfo.CurrentCulture` directly bypasses the multi-scope model and may get stale after thread pool reuse. Always use `I18NContext` or the extension methods.

⚠️ **Option C is for formatting inside a scope, and not for choosing one.** With no scope open
`I18NContext.Current` falls back to the thread's culture, and `WithCultureAsync` does not restore it
after an await — so "what language when nobody said?" answered from there is whatever the last piece of
work on that thread left behind (measured: a letter in Italian for an applicant who named no language,
because another test had rendered an Italian one on the same thread). That question has one contract:
`IConfiguredCultures.Default`, registered by `UseI18N` — see
[Concepts](/modules/i18n/concepts/#fallback-behavior).

---

## 5. Forgetting to Include Translation Files as AdditionalFiles

**Wrong:**

```xml
<!-- .csproj -->
<ItemGroup>
  <!-- Files exist in translations/ but are not included -->
</ItemGroup>
```

```csharp
[assembly: TranslationKeys]
// Compiles fine, but T class has no properties
```

**Compile result:** No errors, no warnings. The source generator finds no `AdditionalFiles` matching recognized translation folders, so it generates an empty `T` class (or no class at all). You only discover the problem when `T.Welcome` does not exist.

**Right:**

```xml
<!-- .csproj -->
<ItemGroup>
  <AdditionalFiles Include="translations/*.json" />
  <!-- Or for folder-per-culture: -->
  <AdditionalFiles Include="translations/**/*.json" />
</ItemGroup>
```

**Why:** The source generator only sees files explicitly listed as `AdditionalFiles` in the project. Regular `Content` or `None` items are invisible to Roslyn analyzers and source generators. The `Include` pattern must match your actual folder structure. Use `*.json` for flat structures and `**/*.json` for folder-per-culture layouts.

---

## 6. Not Capturing I18NContext in Tests

**Wrong:**

```csharp
[Fact]
public void PriceFormatting_Italian_UsesComma()
{
    I18NContext.SetCulture("it-IT");
    var price = Money.From(1234.56m, CurrencyCode.EUR);
    price.Format().Should().Contain(",");
}

[Fact]
public void PriceFormatting_English_UsesDot()
{
    // Oops -- if tests run in parallel, it-IT from the previous test may leak
    I18NContext.SetCulture("en-US");
    var price = Money.From(1234.56m, CurrencyCode.EUR);
    price.Format().Should().Contain(".");
}
```

**Runtime result:** Tests pass individually but fail when run in parallel. `AsyncLocal` isolates between async flows, but `I18NContext.SetCulture()` also syncs with `Thread.CurrentThread.CurrentCulture`, which can leak across tests sharing the same thread pool thread.

**Right:**

```csharp
// Option A: TestI18NScope (recommended -- disposable, automatic cleanup)
[Fact]
public void PriceFormatting_Italian_UsesComma()
{
    using var _ = new TestI18NScope(CultureCode.Italian);
    var price = Money.From(1234.56m, CurrencyCode.EUR);
    price.Format().Should().Contain(",");
}

// Option B: TestI18N static helper
[Fact]
public void PriceFormatting_English_UsesDot()
{
    TestI18N.WithCulture(CultureCode.English, () =>
    {
        var price = Money.From(1234.56m, CurrencyCode.EUR);
        price.Format().Should().Contain(".");
    });
}

// Option C: Manual capture/restore
[Fact]
public void PriceFormatting_German()
{
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
}
```

**Why:** `TestI18NScope` and `TestI18N.WithCulture` capture the `AsyncLocal` context, both thread cultures (`CurrentCulture` and `CurrentUICulture`), and restore all three when the scope ends. This guarantees test isolation regardless of parallel execution.

---

## 7. Using LocalizedString for Application Strings

**Wrong:**

```csharp
public class ShippingNotifier
{
    // Application strings hardcoded as LocalizedString instances
    private static readonly LocalizedString ShippedSubject = LocalizedString.From(
        ("en", "Your order has shipped"),
        ("it", "Il tuo ordine è stato spedito"));

    public string SubjectFor(Order order) => ShippedSubject.Value;
}
```

**Runtime result:** Works, but the translations are scattered across C# code instead of centralized JSON files. No build-time completeness checking. No way to see which translations are missing. Adding a new language requires touching every file that defines a `LocalizedString`.

**Right:**

```csharp
// translations/en.json
// { "emails": { "orderShipped": "Your order has shipped" } }

// translations/it.json
// { "emails": { "orderShipped": "Il tuo ordine è stato spedito" } }

// Code uses the generated T class
public class ShippingNotifier
{
    public string SubjectFor(Order order) => T.Emails.OrderShipped.Value;
}
```

**Why:** `LocalizedString` is designed for per-entity database content (product names, descriptions) where translations vary per record. Application strings (UI labels, email subjects) should use the generated `T` class because: (a) translations are centralized in JSON files, (b) the SG validates completeness at build time via PRAG1802 warnings, (c) adding a new language means adding one JSON file, not modifying dozens of C# files.

Error messages are neither: an error carries no text. Return `NotFoundError.For("Order", id)` and the
response is localized from the error's `Code` by the registered `IErrorMessageResolver` — see
[Result localization](/modules/result/localization/).

---

## 8. Not Rounding Money to Currency Minor Units

**Wrong:**

```csharp
var subtotal = Money.From(33.33m, CurrencyCode.USD);
var quantity = 3;
var total = subtotal * quantity; // $99.99

// Tax calculation produces extra decimals
var tax = total * 0.0875m; // $8.749125
var grandTotal = total + tax; // $108.739125 -- too many decimals!

return grandTotal; // Stored in DB, shown in UI with 6 decimal places
```

**Runtime result:** The `Money` type preserves decimal precision. Without explicit rounding, you end up with amounts like `$108.739125` that look wrong in invoices, cause penny discrepancies in accounting, and may fail database column precision constraints.

**Right:**

```csharp
var subtotal = Money.From(33.33m, CurrencyCode.USD);
var total = subtotal * 3;
var tax = (total * 0.0875m).RoundToMinorUnit(); // $8.75 (USD has 2 minor units)
var grandTotal = total + tax; // $108.74

// For JPY (0 minor units):
var yenPrice = Money.From(1234.5m, CurrencyCode.JPY);
var rounded = yenPrice.RoundToMinorUnit(); // 1235 (JPY rounds to integers)
```

**Why:** `RoundToMinorUnit()` uses the currency's ISO 4217 minor units (USD = 2, JPY = 0, BHD = 3) to round the amount correctly. Always round after multiplication or division operations. Round at the boundary: just before display, storage, or aggregation.

---

## 9. Assuming All Cultures Use Left-to-Right Number Formatting

**Wrong:**

```csharp
// Manual string building with assumed formatting
public string FormatOrderSummary(int count, Money total)
{
    return $"{count} items - {total.Amount:N2} {total.Currency.Symbol}";
}
// For ar-SA: "5 items - 1,234.56 ر.س" -- wrong separator, wrong symbol placement
```

**Runtime result:** Hardcoded formatting assumptions produce wrong output for many cultures. Arabic uses different digit grouping. German puts the currency symbol after the amount. Indian numbering uses lakh/crore grouping (1,23,456).

**Right:**

```csharp
public string FormatOrderSummary(int count, Money total)
{
    var itemText = localizer.Plural("items", count);
    var totalText = total.Format(); // Culture-aware: symbol, separators, position
    return $"{itemText} - {totalText}";
}
```

**Why:** The extension methods and `GlobalizationFormatter` delegate to .NET's `CultureInfo` for culture-specific number formatting (decimal separators, thousand grouping, currency symbol placement). Manual string concatenation with `{amount} {symbol}` assumes en-US conventions. Always use `Format()` or `FormatNumber()` for culture-correct output.

---

## 10. Forgetting the Middleware Order in ASP.NET Core

**Wrong:**

```csharp
var app = builder.Build();
app.UseRouting();
app.MapPragmaticEndpoints();
app.UsePragmaticInternationalization(); // Too late!
app.Run();
```

**Runtime result:** The i18n middleware runs after endpoint handlers have already executed. The `I18NContext` is never set when your handler calls `money.Format()` or `T.Welcome.Value`. Formatting falls back to `CultureInfo.CurrentCulture`, and translations may use the wrong language.

**Right:**

```csharp
var app = builder.Build();
app.UsePragmaticInternationalization(); // Before routing and endpoints
app.UseRouting();
app.MapPragmaticEndpoints();
app.Run();
```

**Why:** The `I18NContextMiddleware` must run before any code that reads `I18NContext`. It resolves the culture from the request (query string, Accept-Language, provider chain) and sets the `AsyncLocal` context. Middleware executes in the order it is registered. Place `UsePragmaticInternationalization()` early in the pipeline, before routing and endpoint mapping.

---

## 11. Using string.Format Instead of StringLocalizer for Interpolation

**Wrong:**

```csharp
// translations/en.json: { "greeting": "Hello, {0}! You have {1} messages." }
var template = localizer["greeting"];
var message = string.Format(template, userName, count);
```

**Runtime result:** Works for simple cases, but positional placeholders (`{0}`, `{1}`) are fragile. If a translator reorders them (common in RTL languages), the arguments no longer match. And `string.Format` does not handle plurals.

**Right:**

```csharp
// translations/en.json: { "greeting": "Hello, {name}! You have {count} messages." }
var message = localizer["greeting", userName, count];
// StringLocalizer handles named parameter replacement
```

**Why:** `StringLocalizer` supports named placeholders and positional arguments. Named placeholders are self-documenting for translators and order-independent. The localizer also handles plural forms via `localizer.Plural()` which integrates with `PluralRules` for the current culture.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| No culture set before formatting | Inconsistent formatting based on server locale |
| Mixing currencies in arithmetic | `InvalidOperationException` at runtime |
| Hardcoded plural forms | Wrong plurals in non-English languages |
| `CultureInfo.CurrentCulture` directly | Stale culture after thread pool reuse |
| Missing `AdditionalFiles` in csproj | Empty `T` class, no compile errors |
| No test isolation for I18NContext | Tests fail when run in parallel |
| `LocalizedString` for app strings | Scattered translations, no completeness checking |
| Not rounding to minor units | Penny discrepancies, wrong decimal places |
| Hardcoded number formatting | Wrong separators and symbol placement |
| Wrong middleware order | Formatting uses server default culture |
| `string.Format` for translations | Fragile positional placeholders |
