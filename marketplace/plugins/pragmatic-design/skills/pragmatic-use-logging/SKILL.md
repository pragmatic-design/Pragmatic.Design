---
name: pragmatic-use-logging
description: Use when configuring logging providers (app.UseLogging), adding structured high-performance logging with [LoggerMessage], or naming Activity traces; Pragmatic.Logging.
---

# Pragmatic Use Logging

**Covers:** Logging and observability in a Pragmatic.Design app: provider configuration with Pragmatic.Logging (app.UseLogging), high-performance logging with [LoggerMessage], Activity naming.

`Pragmatic.Logging` provides logging providers (rich console, file, JSON/NDJSON, enrichment, redaction, audit). Unlike caching or jobs it is **not auto-wired**: the host must configure it explicitly with `app.UseLogging`.

## When to use

- You are setting up logging for a Pragmatic app.
- You are adding structured, high-performance logs to domain code.

## Package

```xml
<PackageReference Include="Pragmatic.Logging" Version="1.0.0-alpha.1" />
```

## Configuring logging (host)

In `Program.cs`, inside the `PragmaticApp.RunAsync` callback:

```csharp
using Pragmatic.Logging.Extensions;   // UseLogging, AddFile/AddConsole overloads
using Pragmatic.Logging.Providers;    // PragmaticConsoleConfiguration

await PragmaticApp.RunAsync(args, app =>
{
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
        log.AddFile("logs/app-{Date}.log", cfg =>
        {
            cfg.MinimumLevel = LogLevel.Debug;
            cfg.Formatting.UseUtcTimestamp = true;
        });
    });
}).ConfigureAwait(false);
```

Providers available on the builder: `AddConsole`, `AddFile`, `AddJson`, `AddNdjsonAsync` (async NDJSON with batching), `AddProvider<T>` (custom). Global options and presets on the same builder: `Configure(o => …)`, `UseDevelopmentPreset()`, `UseProductionPreset()`, `UseHighPerformancePreset()`, `UseCompliancePreset(standard)`, `EnableDataRedaction(…)`, `EnableAuditTrail(…)`, `EnableRateLimiting(…)`. Ready-made console configurations: `PragmaticConsoleConfiguration.ForDevelopment()`, `ForCiCd()`, `ForAdvancedConsole()`, `ForHighContrast()`, `ForMonochrome()`.

`UseLogging` replaces `ILoggerFactory` with the Pragmatic one, preserving existing providers. Without a Pragmatic host, the entry points are named for what they do:

| Call | What it does |
|---|---|
| `services.AddPragmaticLogging(b => ...)` | The canonical one: registry, context manager, global filters, and `PragmaticLoggerFactory` in place of `ILoggerFactory`. Configure providers on the builder |
| `services.AddPragmaticLoggingWithOptions(o => ...)` | The same registration, plus `PragmaticLoggingOptions` set in code |
| `services.AddPragmaticLoggingFromConfiguration(config)` | The same, with those options bound from `IConfiguration` |
| `services.AddPragmaticHttpLogging()` | ASP.NET only: HTTP context and correlation-id providers. Decorates the others, does **not** wire logging on its own |
| `services.AddPragmaticLoggingAugmentation(b => ...)` | Adds providers **without** replacing the factory |

The presets (`AddPragmaticLoggingForMicroservice`, `...WithSmartPreset`, and the rest) perform the full registration too.

There is one builder, `Pragmatic.Logging.Extensions.PragmaticLoggingBuilder`: start from
`UseLogging` or `services.AddPragmaticLogging`.

Optional HTTP middleware: `app.UsePragmaticLogging()` adds correlation ID and request-context enrichment; `app.UsePragmaticBaggage()` propagates W3C Baggage.

## Logging in code: use `[LoggerMessage]`

For logs in domain code use the **standard .NET** `[LoggerMessage]` source generator (`Microsoft.Extensions.Logging`), which produces allocation-free logs at compile-time:

```csharp
public sealed partial class OrderService(ILogger<OrderService> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} confirmed for {CustomerId}")]
    private partial void LogOrderConfirmed(Guid orderId, Guid customerId);

    public void Confirm(Order order)
    {
        // ...
        LogOrderConfirmed(order.Id, order.CustomerId);
    }
}
```

Never use interpolated logging (`logger.LogInformation($"...")`): it allocates and loses structure. `[LoggerMessage]` is the mandatory pattern.

> `Pragmatic.Logging` works entirely through the standard `ILogger`; there is no proprietary logging-method attribute. Use Microsoft's `[LoggerMessage]` for source-generated hot-path logging.

## Observability

- **Activity / tracing**: name Activities `{Module}.{Operation}` (e.g. `Sales.PlaceOrder`). Pragmatic modules already expose an `ActivitySource` and OpenTelemetry metrics; configure an OTel exporter in the host to collect them.
- **Levels**: `Debug`/`Trace` for development detail, `Information` for business events, `Warning`/`Error` for problems. Set `MinimumLevel` per provider.
- **Correlation**: with `app.UsePragmaticLogging()` every log within a request carries the same correlation ID, which is essential for production debugging.

## What must not reach the log

Two mechanisms, governed differently on purpose:

| | By declaration | By shape |
|---|---|---|
| What | members marked `[NotLogged]` (`Pragmatic`, in Abstractions) or `[PersonalData]` | e-mail, IBAN, card and account numbers, national ids, CVV, bearer tokens, `password=`… |
| How | the generator emits a redaction map per type; `RedactingLoggerFactory` masks those members in every provider | pattern matching on the text (`Pragmatic.Redaction.PersonalDataRedactor`, and the logging pipeline's own set) |
| Switch | none: wired by the generated host whenever a map exists, in every environment | `EnableDataRedaction(…)` / the compliance presets on the logging builder |
| Guarantee | exact, for what is declared | a floor: it cannot recognise a name, an address, or a sentence about someone's health |

⚠️ **Declared redaction acts on a structured argument whose type carries the declaration.** Logging an
`Order` whose `CustomerEmail` is `[NotLogged]` masks it; logging `order.CustomerEmail` as a `string`
parameter does not: a string has no declared type, and only the patterns stand between it and the
file. Log identifiers, not values: `LogOrderConfirmed(order.Id)`, never the e-mail. Scopes
(`BeginScope`) are not redacted.

Free text (a note, a description) is not personal data to classify; it is text that may contain some:
mark it `[NotLogged]` (`pragmatic-use-privacy`).

## Context on every line

Correlation id, HTTP request data, machine and process come from built-in context providers. Add your
own (the tenant, the plan) by extending `ContextProviderBase`, and read request state **when the line is
written**, not in the constructor: the context manager is a singleton, so a provider that captured
`ITenantContext` would stamp the first tenant it saw on every line for the life of the process.

```csharp
public sealed class TenantLogContext(IHttpContextAccessor http) : ContextProviderBase("Tenant", priority: 80)
{
    public override bool IsAvailable() => Current() is { IsResolved: true };
    public override IReadOnlyDictionary<string, object?> GetContextProperties()
        => CreatePropertiesDictionary(("TenantId", Current()?.TenantId));
    private ITenantContext? Current() => http.HttpContext?.RequestServices.GetService<ITenantContext>();
}

app.UseLogging(log => log.ConfigureContext(ctx => ctx.AddProvider<TenantLogContext>()));
```

Lower `Priority` wins when two providers supply the same key. ⚠️ Outside an HTTP request (a message
handler, a job) there is no `HttpContext`: the line carries no tenant from this provider, which is what
`IsAvailable` reports.

## Asserting on it: `CapturedLogs`

A log line nobody asserts on is a line that can stop naming the right thing without anyone noticing.
`Pragmatic.Testing` carries an `ILoggerProvider` for exactly this:

```csharp
var logs = new CapturedLogs();
builder.ConfigureLogging(l => l.AddProvider(logs));   // WebApplicationFactory, or any host

logs.PropertyOf("Story", "ActorId").Should().Be("agent-7");
logs.Contains(LogLevel.Warning, "quota").Should().BeTrue();
logs.Clear();                                          // a fixture shared across tests needs this
```

⚠️ **Assert on the structured property, not the sentence.** The whole value of `[LoggerMessage]` is
the named fields; a test matching rendered text stays green while the property carrying the meaning
disappears. `PropertyOf` therefore requires the line to *have* the property, not merely to contain
the words; matching text alone picks up neighbours (an invoker's own "Action WriteStoryAction
succeeded" contains `Story`) and returns null, which reads exactly like "it was never logged".

Worth asserting on, in order of how often the answer matters: **who acted** (under delegation
`ICurrentUser.Id` is the subject and `Delegation.ActorId` is the actor, and a line with only one of
them cannot answer "who did this"); the **tenant**; and the identifier of the thing that changed.

## What to enable / what to avoid

| Enable | Avoid |
|---|---|
| `app.UseLogging(...)` with at least one provider | Leaving logging unconfigured (defaults to stock ASP.NET logging) |
| `AddConsole` in dev, `AddFile`/`AddNdjsonAsync` in prod | `AddConsole` as the only provider in production |
| `[LoggerMessage]` (Microsoft) for logs in code | Hand-written `LoggerMessage.Define(...)` boilerplate |
| `app.UsePragmaticLogging()` for correlation ID | Interpolated logging `$"..."` |

## Build verification

```powershell
dotnet build
dotnet run --project src\App.Host       # verify that logs appear with the format of the chosen provider
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase, Invoicing and Time off
example applications (code that compiles and that `TimeOff.IntegrationTests`,
`Invoicing.IntegrationTests` and `Showcase.IntegrationTests` exercise) and kept identical to it by the
gate: the host's `UseLogging`, `[LoggerMessage]` in code, `[NotLogged]` on a secret, and a test that
asserts the declared redaction through the application's own logger factory.

Context providers (`ContextProviderBase`) and `CapturedLogs` are used by no tested application yet, so
there is no example of them here: the sections above are the reference.
