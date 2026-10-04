# Integration: ASP.NET Core, Pragmatic Host, EF Core

How to wire Pragmatic.Internationalization into a web application: DI registration, the culture
middleware, the fluent `I18NBuilder`, the translation endpoint, localized ProblemDetails, and
EF Core persistence.

## ASP.NET Core (plain)

### When you need it

Any web app that must resolve the request culture and format/localize accordingly.

### What you write

```csharp
builder.Services.AddPragmaticInternationalization(options =>
{
    options.DefaultUICulture = CultureCode.EnglishUS;
    options.SupportedCultures = [CultureCode.EnglishUS, CultureCode.Italian];
});

var app = builder.Build();
app.UsePragmaticInternationalization();   // culture middleware, before routing
```

Or bind options from configuration (section `"I18N"` by default):

```csharp
builder.Services.AddPragmaticInternationalization(builder.Configuration);
```

### What you get

`AddPragmaticInternationalization` registers:

| Service | Lifetime | Role |
|---------|----------|------|
| `SystemConfigProvider` | singleton | fallback config provider (Priority 0) reading `I18NOptions` |
| `I18NConfigResolver` | scoped | merges all `II18NConfigProvider`s by priority |
| `GlobalizationFormatter` | scoped | culture-aware formatting, follows `I18NContext.Current` |
| JSON converters | n/a | all i18n types on `ConfigureHttpJsonOptions` (see below) |

It returns an `I18NBuilder` for further fluent configuration (see below).

`UsePragmaticInternationalization()` adds `I18NContextMiddleware`, which per request:

1. Resolves the configuration from the provider chain (`I18NConfigResolver`).
2. Applies a request culture override: query string (`options.QueryStringKey`, default `culture`)
   first, then `Accept-Language` (quality-ordered best match), both validated against
   `SupportedCultures`.
3. Sets `I18NContext` (and thread cultures), opens an `I18N.Context.Resolve` activity.
4. Captures and restores the previous thread cultures after the request: no culture leaks
   between requests on the same thread.

## Pragmatic Host

### When you need it

In a Pragmatic.Composition host, the middleware is auto-wired for you by
`InternationalizationStep` (an `IStartupStep` with `Order = 95`, after authentication, so a provider
that reads the signed-in user's preference sees who is asking).

### What you write

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS)
            .Support(CultureCode.EnglishUS, CultureCode.Italian);
        i18n.AddJsonTranslations("localization", watchForChanges: true);
        i18n.LocalizeProblemDetails();
    });
});
```

### What you get

`UseI18N` calls `AddPragmaticInternationalization()` and hands you the same `I18NBuilder`.
No manual `UsePragmaticInternationalization()` needed: the startup step adds the middleware.

### Where the culture comes from when you configure none

The modules' translations. The host registers a `DeclaredLanguagesConfigProvider` (priority −100, below
everything else) from their translation metadata: the supported cultures are those of their
`translations/{culture}.json` files, and the default is the culture they are written from
(`[TranslationKeys(DefaultCulture = …)]`, `en` unless set) when every module names the same one and has a
file for it. `DefaultCulture(...)`, `Support(...)` and the `I18N` configuration section override it.

There is no silent fallback beyond that. When no default comes from anywhere, `UsePragmaticInternationalization`
throws `I18NConfigurationException` while the pipeline is built, so **the host does not start**; it used
to start and fail every request instead. The check runs only when every registered provider is static (the
options and the declared languages): one that answers per request (tenant, user, database) may supply
the default, and then the request decides, as before.

## I18NBuilder Reference

| Method | Effect |
|--------|--------|
| `DefaultCulture(CultureCode)` | sets `I18NOptions.DefaultUICulture` |
| `Support(params CultureCode[])` | sets `I18NOptions.SupportedCultures` |
| `AddScope<TScope>()` | registers a typed `ICultureScope` default culture |
| `AddConfigProvider<TProvider>()` | registers an `II18NConfigProvider` (scoped) |
| `AddConfigProvider(II18NConfigProvider)` | registers a provider instance (singleton) |
| `AddConfigProvider(Func<IServiceProvider, II18NConfigProvider>)` | registers a provider factory (scoped) |
| `AddLocalizationProvider<TProvider>()` | registers an `ILocalizationProvider` (singleton) |
| `AddLocalizationProvider(ILocalizationProvider)` | registers a provider instance |
| `AddJsonTranslations(basePath?, watchForChanges)` | registers `JsonLocalizationProvider` (default path: `I18NOptions.TranslationsPath`, `"translations"`) |

### How a culture finds its file

`JsonLocalizationProvider` reads `{culture}.json` from the base path, and falls back one
subtag at a time when there is none: `zh-Hant-CN` asks `zh-Hant.json`, then `zh.json`. So a
host that declares `en-US` and ships `en.json` resolves, and one that ships both keeps the
more specific file - the exact name always wins.

A declared culture that nothing covers, not even through its language, is reported once as a
warning when the provider loads. It is a warning and not a failure: a host that supports four
languages and ships three is wrong about one of them, not unable to start.

| `LocalizeProblemDetails()` | replaces `IErrorMessageResolver` with `LocalizedErrorMessageResolver` |

## Configuration Providers (per-tenant / per-user culture)

### When you need it

The culture must come from data (the tenant's settings, the user profile), not from static options.

### What you write

Implement `II18NConfigProvider` with a priority above 0; derive from `CachedConfigProvider`
to get TTL caching (default 5 minutes) and invalidation for free:

```csharp
public sealed class TenantConfigProvider(ITenantContext tenant) : CachedConfigProvider
{
    public override int Priority => 100;                       // beats SystemConfigProvider (0)
    protected override string GetCacheKey() => tenant.Id;
    protected override I18NConfig? LoadConfiguration() =>
        new() { DefaultUICulture = tenant.Culture };           // null properties = defer to lower priority
}
```

```csharp
i18n.AddConfigProvider<TenantConfigProvider>();
```

### What you get

`I18NConfigResolver` merges all providers highest-priority-first; each `null` property in an
`I18NConfig` defers to the next provider down, ending at `SystemConfigProvider` (the options).
Suggested priority convention: request 300 · user 200 · tenant 100 · system 0 (only the system
provider is built in; the others are yours).

## Translation Endpoint (frontend feed)

### When you need it

A SPA/frontend needs the same translations the backend knows, without duplicating JSON files.

### What you write

```csharp
app.MapPragmaticTranslations();                       // GET /api/i18n/{culture}
app.MapPragmaticTranslations("/api/translations", cacheDurationSeconds: 600);
```

### What you get

`GET /api/i18n/it` returns all merged translations from every registered
`ILocalizationProvider` (via `CompositeLocalizationProvider`) for that culture, as a flat
`key → value` map. Supports `?prefix=error.validation` for lazy-loading a subset per
boundary/page. Responses carry `Cache-Control: public, max-age=300` (configurable). Returns
500 if no localization provider is registered.

## Localized ProblemDetails

### When you need it

Domain errors (`Result` failures) must reach the client in the caller's language.

### What you write

```csharp
i18n.LocalizeProblemDetails();
```

Translation keys are the error's `MessageKey` (or, when resolving from a bare code,
`error.` + the code lowercased with `_` → `.`) plus a suffix: `.detail` for the message,
`.title` for the title. Never the bare key: a key that is also the prefix of another cannot become
both a member and a nested class of the generated key class, and is reported as `PRAG1805`.

```json
{
  "error.room.unavailable.detail": "Room is not available for the selected dates",
  "error.room.unavailable.title": "Room Not Available"
}
```

### What you get

`LocalizedErrorMessageResolver` resolves ProblemDetails `detail` from `{messageKey}.detail`
and `title` from `{messageKey}.title`, in the current request culture, with `{param}`
placeholders interpolated from `Error.Parameters` (`"Cannot exceed {limit} nights"` +
`Parameters["limit"] = 14` → `"Cannot exceed 14 nights"`). Missing keys fall back to the
error's own message.

A validation failure also carries one message per issue. `errors` keeps the keys (what a client
matches on, the same in every language) and `messages` beside it holds their words in the
caller's language, aligned one for one. Each issue's key is looked up as it is, no suffix, with
the issue's parameters (`"validation.maxlength": "At most {max} characters"`):

```json
{
  "code": "VALIDATION_ERROR",
  "errors":   { "to": ["validation.leave_request.ends_before_it_starts"] },
  "messages": { "to": ["Il periodo finisce prima di cominciare."] }
}
```

A key with no translation keeps its place in `messages` as the key; with none translated at all,
there is no `messages`.

## JSON Converters

Registered automatically on ASP.NET Core's JSON options by `AddPragmaticInternationalization`.
For a custom `JsonSerializerOptions`:

```csharp
var options = new JsonSerializerOptions();
options.AddPragmaticInternationalization();
```

Covered types (plus their nullable variants): `Money` (`{ "amount": 99.99, "currency": "USD" }`),
`CurrencyCode`, `LanguageCode`, `CountryCode`, `CultureCode` (as ISO/BCP-47 strings),
`LocalizedString` (as a `{ "en": "...", "it": "..." }` map).

## Validation Attributes

Three rules from `Pragmatic.Validation.Attributes`, generated like any other; no registration needed.
The type is `partial` because the generator writes `Validate()` into it:

```csharp
public sealed partial record CreateInvoiceRequest
{
    [Required, PositiveMoney]                          // Amount > 0 (null passes; add [Required])
    public Money? Total { get; init; }

    [NonNegativeMoney]                                 // Amount >= 0
    public Money? Discount { get; init; }

    [SupportedCurrency("EUR", "USD")]                  // whitelist (Money or CurrencyCode)
    public CurrencyCode Currency { get; init; }
}
```

## EF Core

### What you write

Either per-context:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyPragmaticInternationalization();
}
```

or convention-based (EF Core 6+):

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.ApplyPragmaticInternationalizationConventions();
}
```

### What you get

| CLR type | Column mapping |
|----------|----------------|
| `LocalizedString` | JSON string (`{"en":"...","it":"..."}`), empty on null/empty |
| `CurrencyCode` / `CurrencyCode?` | `varchar(3)` ISO code |

### Money: manual mapping

`Money` has **no automatic converter**: a single-column mapping would lose either precision
or the currency. Map it as two columns; `MoneyConfiguration.DefaultPrecision` (19) and
`MoneyConfiguration.DefaultScale` (4) are the recommended constants:

```csharp
builder.OwnsOne(i => i.Total, money =>
{
    money.Property(m => m.Amount).HasPrecision(MoneyConfiguration.DefaultPrecision,
                                               MoneyConfiguration.DefaultScale);
    money.Property(m => m.Currency).HasMaxLength(3);
});
```

## Observability

The module emits OpenTelemetry-friendly signals, all named under `Pragmatic.Internationalization`:

- **ActivitySource**: the middleware opens an `I18N.Context.Resolve` activity per request,
  tagged with the resolved cultures.
- **Meter**: counters `pragmatic.i18n.key_lookups` and `pragmatic.i18n.missing_keys` track
  translation lookups and misses at runtime (`StringLocalizer`).

Enable richer tracing with `I18NOptions.EnableDiagnostics = true`, and subscribe your OTel
setup to the `Pragmatic.Internationalization` source/meter names.

## See Also

- [Getting Started](getting-started.md): culture context, Money, formatting
- [Translation Keys](translation-keys.md): the generated `T` class and localization providers
- [Troubleshooting](troubleshooting.md): diagnostics and common failures
- `Pragmatic.Temporal.Internationalization`: culture-aware formatting for the Temporal types (`LocalDateTime`, `ZonedDateTime`); `LocalDate`/`LocalTime` work out of the box via `DateOnly`/`TimeOnly` conversions
