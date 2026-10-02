---
name: pragmatic-use-i18n
description: Use when the app needs several languages or cultures, localized error messages, money and currencies, or culture-aware formatting — Pragmatic.Internationalization, the generated T class, Money, the culture middleware.
---

# Pragmatic Use I18N

**Covers:** Internationalization with Pragmatic.Internationalization from NuGet — async-safe culture context, compile-time-checked translations (the generated T class), Money/CurrencyCode value types, culture-aware formatting, and the ASP.NET culture middleware.

Five pillars: an `AsyncLocal` `I18NContext` that flows across `await`; `Money`/`CurrencyCode` value
types; culture-aware formatting; **compile-time-checked translations** (a generated static `T` class);
and humanizers. Missing translations are build warnings (PRAG1802).

## When to use

- Localized UI/API/error text; multi-scope culture (UI vs data vs documents).
- Money arithmetic + formatting; culture-aware numbers/dates.

## Packages

```xml
<PackageReference Include="Pragmatic.Internationalization" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.Internationalization.AspNetCore" Version="1.0.0-alpha.*" />  <!-- middleware -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.*">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets><PrivateAssets>all</PrivateAssets>
</PackageReference>
```

## Translations (source-generated)

1. Add `translations/en.json`, `translations/it.json`, … (one per culture).
2. In the `.csproj`: `<AdditionalFiles Include="translations/*.json" />`.
3. Use the generated `T` class:

```csharp
var text    = T.Welcome.Value;           // current culture
var italian = T.Common.Welcome["it"];    // a specific culture
return NotFoundError.For("Order", id);    // an error carries no text: see below
```

An error is localized from its `Code` (and a validation issue from its message key) by the registered
`IErrorMessageResolver` at the response boundary — never by passing a `T.*` string into the error.

`T`'s members are properties, so an attribute cannot take one. For those places the generator writes
**`TKeys`** (`{ClassName}Keys`) — the same hierarchy, each key a `const string` — and a validation rule
names its message through it instead of repeating the key as a string, so a renamed JSON key is a
compile error:

```csharp
[GreaterThanOrEqualProperty(nameof(From), MessageKey = TKeys.Validation.LeaveRequest.EndsBeforeItStarts)]
public required DateOnly To { get; init; }
```

## Money

```csharp
var price = Money.From(99.99m, CurrencyCode.EUR);
price.Format();   // "99,99 €" (it-IT) / "€99.99" (en-US)   — same-currency arithmetic enforced
```

**On the wire, an amount needs its converter, and you get it by putting the amount there.**
`Money` travels as `{ "amount": 99.99, "currency": "EUR" }` through `MoneyJsonConverter` and its
siblings, which `AddPragmaticInternationalization` installs into the HTTP JSON options. A host that
calls `UseI18N`, or whose modules have a `translations/` file, gets that call and them with it; and a
module that puts one of the i18n value types on an endpoint's shape — the result, a request member, or
a member of either — gets the **converters alone**, because the shape is a declaration too and the
generated host reads it.

⚠️ Without the converters every request carrying an amount is a **400 before any rule runs**. Where
the generator has no endpoint to read — a background worker, a message handler, a client — install them
by hand:
`app.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.AddPragmaticInternationalization());`.

**Validating an amount** — `Pragmatic.Validation.Attributes`, generated like every other rule:

```csharp
[Required, PositiveMoney]          public Money? Total { get; init; }      // > 0; null passes, hence [Required]
[NonNegativeMoney]                 public Money? Discount { get; init; }   // >= 0
[SupportedCurrency("EUR", "USD")]  public CurrencyCode Currency { get; init; }   // Money or CurrencyCode
```

**Storing it.** On an entity in a Pragmatic host, a `Money` property is mapped by the generator as two
columns, `{Property}_Amount` and `{Property}_Currency` — nothing to configure. In a DbContext you write
yourself, map it as an owned type with `MoneyConfiguration.DefaultPrecision`/`DefaultScale` (19, 4):
there is no single-column converter, because one column loses either precision or the currency.

## Translated data — `LocalizedString`

The `T` class is for the application's own strings. Text **users** write in several languages — a
product name, a category description — is a `LocalizedString` on the entity, stored as one JSON column
(`{"en":"…","it":"…"}`) and read in the current culture with `.Value` (or `name["it"]`), falling back
along the culture chain. With `Pragmatic.Internationalization.EFCore` referenced the generated DbContext
applies the mapping (`ApplyPragmaticInternationalization()`); on the wire it travels as the same map.

## Plurals

A key whose value is an object keyed by CLDR category is a plural — `"items": { "one": "1 item",
"other": "{count} items" }` — resolved with `localizer.Plural("items", count)`. A missing category falls
back to `other`; a missing `other` renders **empty**, so every plural key needs one. The categories a
language uses differ (`few`, `many` in Polish, Russian, Arabic): write the ones the language has, not the
English pair.

## Host wiring

**The cultures come from the translations.** A Pragmatic host whose modules have
`translations/{culture}.json` supports those cultures, and defaults to the one they are written from —
`[TranslationKeys(DefaultCulture = "en")]`, `en` unless set, and it must have its own file. Write
`UseI18N` only to choose something else — regional cultures, a different default — or to add translations
and localized errors:

```csharp
app.UseI18N(i18n =>
{
    i18n.DefaultCulture(CultureCode.EnglishUS);   // Pragmatic.Internationalization.Types — overrides the declared one
    i18n.Support(CultureCode.EnglishUS, CultureCode.Italian);
    i18n.LocalizeProblemDetails();                // optional: localized error responses
});
```

⚠ There is no silent fallback culture. With no default from the application (`UseI18N`, the `I18N`
section) nor from the translations — none, or modules written from different languages — the host
**refuses to start** with an `I18NConfigurationException`, instead of failing every request. A provider
that answers per request (tenant, database) defers that check to the request. The middleware sets
`I18NContext` from `Accept-Language` / query / a provider.

**Where it runs, and why it matters for a 403.** The generated host puts the culture middleware
**after authentication and tenant resolution, and before authorization** (`InternationalizationStep`
at 93, `AuthorizationStep` at 94): late enough that a provider reading the signed-in user's own
language sees an authenticated caller, early enough that the refusal the authorization middleware
writes has a language. That refusal is the only error an application answers without an endpoint;
written before a culture exists, it would fall back to `CultureInfo.CurrentCulture` — the machine's
language on a developer's box, English on a CI runner. A custom `IStartupStep` that localizes anything
should sit after 93.

**"What language when nobody said?" is `IConfiguredCultures`, not the ambient culture.** A module that
has to pick a language with no request to read it from — a job, a document, a letter for somebody who
named none — injects `IConfiguredCultures` (`Pragmatic.Internationalization.Context`, registered by
`UseI18N`, **scoped**) and reads `Default`. ⚠️ The three wrong answers, in order of how convincing they
look:

| ❌ | why it is wrong |
|---|---|
| `I18NContext.Current.Culture` | with no scope open it falls back to `CultureInfo.CurrentCulture`, and `WithCultureAsync` does **not** restore the thread's culture after an await — so outside a scope it is whatever the last piece of work on that thread left behind: a letter can come out in Italian because other work rendered an Italian one on the same thread |
| a `const "en-US"` in the module | honest about the module's own translation file, and silently divergent the day the host is configured differently |
| `IStringLocalizer` alone | it resolves against the ambient culture, so it inherits the first row |

`I18NConfigResolver` is **not** the contract to inject: it carries the provider merge and the
validation, and it says so by not being `[ProvidedByHost]` — a module that asks for it still gets
`PRAG1641`.

**The signed-in user's own language is generated — do not write a provider for it.** On the
`[PragmaticUser]` entity (with `Pragmatic.Identity.Persistence`), a `[ProfileProperty]` named exactly
**`PreferredCulture`** (a culture string, `it-IT`) makes the generator write
`{User}CultureConfigProvider` — priority 200, below an explicit `?culture=`/`Accept-Language`, above the
default, cached per user. It is off until the host turns it on:

```csharp
using MyApp.Module.Generated;   // {ModuleAssembly}.Generated
app.UseUserCulture();
```

Another property name (`PreferredLanguage`, `Locale`) is not recognised and nothing is generated. After
the user changes the value, drop their cached entry: `provider.InvalidateCache()` on the
`{User}CultureConfigProvider` resolved from `IEnumerable<II18NConfigProvider>`.

## Runtime translations & frontend feed (optional)

```csharp
i18n.AddJsonTranslations("localization", watchForChanges: true);  // runtime JSON provider (hot reload)
app.MapPragmaticTranslations();   // GET /api/i18n/{culture}?prefix=... → flat key→value map for the SPA
```

`LocalizeProblemDetails()` resolves the detail from **`Error.MessageKey` plus `.detail`** and the
title from the same key plus `.title`, with `{param}` interpolation from `Error.Parameters`. There is
no `error.` prefix added: a `MessageKey` of `edition.full` looks up `edition.full.detail`, and a JSON
key of `error.edition.full.detail` is never found — **silently**, leaving the default text.

⚠️ Never write the bare key beside its `.title`. A key that is also the prefix of another asks the
generated key class for a member and a nested class of the same name; it is reported as `PRAG1805`
and no constant is generated for it.

**`[TranslationKeys(EmbedTranslations = true)]` (the default) makes a module self-sufficient.** Besides
the values in `T`'s properties, the generator writes `TTranslations`, an `ILocalizationProvider` over the
same strings, and every host that includes the module registers it — so `IStringLocalizer`, `t:` in a
template and every lookup by key find the module's translations with nothing copied beside the host.
Plural forms are not embedded: a module that needs them keeps its files on disk, below.

With `EmbedTranslations = false` the `AdditionalFiles` entries feed the compile-time key class only, and
the runtime store is a separate registration: without `AddJsonTranslations(...)` every lookup misses, in
silence.

⚠️ **Two modules, two directories.** `AddJsonTranslations` may be called once per module and every
directory is read — which is the shape to use, because two modules that both copy
`translations/{culture}.json` land on the **same path** in the host's output and one silently
overwrites the other: the host's `en-US.json` is the second module's file, and every message of the
first resolves to its own key. Give each module its own folder in the output and name it in the call:

```xml
<!-- in each module -->
<AdditionalFiles Include="translations/*.json" />
<Content Include="translations/*.json" Link="translations/billing/%(Filename)%(Extension)"
         CopyToOutputDirectory="PreserveNewest" />
```

```csharp
i18n.AddJsonTranslations(Path.Combine(AppContext.BaseDirectory, "translations", "registry"));
i18n.AddJsonTranslations(Path.Combine(AppContext.BaseDirectory, "translations", "billing"));
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Time off example application — code
that compiles and that `TimeOff.IntegrationTests` exercises — and kept identical to it by the gate: a
module's translations and its keys as symbols, rules naming their message by key, translated content with
`LocalizedString`, the user's own language and how it is changed, and the host's `UseI18N`.

Plurals are used by no tested application yet, so there is no example of them here: the sections above
are the reference.
