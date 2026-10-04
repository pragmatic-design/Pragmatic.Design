# Pragmatic.Logging

Feature-rich structured logging for the Pragmatic.Design ecosystem: extends .NET `ILogger` with
privacy-aware redaction, an audit trail, context enrichment, an expression filter DSL, and multiple
providers.

> **Performance:** with every library writing into a sink that renders and reads the event, Pragmatic
> is ahead of Serilog and NLog on simple, `[LoggerMessage]`, structured and high-volume calls, behind
> both with the production preset's context enrichment, and behind ZLogger everywhere. See
> [BENCHMARK-RESULTS.md](BENCHMARK-RESULTS.md) for the run, the machine and the reports.

## The Problem

Production logging needs structured output, PII redaction, correlation IDs, rate limiting, and multiple
targets. `Microsoft.Extensions.Logging` gives you the `ILogger` foundation and
leaves the rest to you. String interpolation allocates on every call (even when the level is off),
secrets leak when someone forgets to sanitize, and context must be threaded by hand.

```csharp
// Without Pragmatic: allocates always, leaks PII + secrets, no context
logger.LogInformation($"Order {orderId} by {email} via {connectionString}");
```

## The Solution

A fluent `PragmaticLoggingBuilder` over standard `ILogger<T>` (no proprietary abstraction) configures
every concern in one place: providers, presets, privacy, performance, context enrichment.

```csharp
builder.Services.AddPragmaticLogging(logging => logging
    .UseProductionPreset()                         // background processing, rate limiting, correlation IDs
    .AddConsole()
    .AddFile("logs/app-{Date}.log")                // rolling file with retention
    .EnableDataRedaction(r => r.SensitivePropertyNames = ["password", "email", "apiKey"]));
```

In a Pragmatic host the same builder is `app.UseLogging(logging => …)`. There is one builder,
`Pragmatic.Logging.Extensions.PragmaticLoggingBuilder`; `ILoggingBuilder.AddPragmaticLogging()` no
longer exists.

Pair it with .NET's built-in `[LoggerMessage]` source generator on hot paths; structured properties,
automatic PII/secret redaction, an audit trail, and context enrichment come built in.

## Installation

```bash
dotnet add package Pragmatic.Logging
```

## Status

**Functional** within 1.0.0-alpha: the builder, the providers (console, rolling file, JSON, NDJSON,
memory, debug, null, Windows Event Log), presets, redaction, the audit trail, rate limiting, and context
enrichment. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | The builder model, presets, zero-allocation infrastructure |
| [Getting Started](docs/getting-started.md) | Configure providers and write your first structured logs |
| [Providers](docs/providers.md) | The eight providers, choosing one, writing your own |
| [Context Enrichment](docs/context-enrichment.md) | Correlation IDs, ambient context, scopes |
| [Privacy & Security](docs/privacy-security.md) | PII/secret redaction, audit trail |
| [Performance](docs/performance.md) | `[LoggerMessage]`, the zero-allocation formatter, rate limiting |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent logging pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem. See [Licensing](../docs/LICENSING.md).
Pragmatic.Logging is **MIT-licensed**.
