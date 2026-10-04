---
title: "Pragmatic.Logging"
description: "Feature-rich structured logging for the Pragmatic.Design ecosystem: extends .NET `ILogger` with"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Logging/README.md
sidebar:
  order: 0
  label: Overview
---
Feature-rich structured logging for the Pragmatic.Design ecosystem: extends .NET `ILogger` with
privacy-aware redaction, an audit trail, context enrichment, an expression filter DSL, and multiple
providers.

> **Performance:** with every library writing into a sink that renders and reads the event, Pragmatic
> is ahead of Serilog and NLog on simple, `[LoggerMessage]`, structured and high-volume calls, behind
> both with the production preset's context enrichment, and behind ZLogger everywhere. See
> [BENCHMARK-RESULTS.md](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Logging/BENCHMARK-RESULTS.md) for the run, the machine and the reports.

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
enrichment. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/logging/concepts/) | The builder model, presets, zero-allocation infrastructure |
| [Getting Started](/modules/logging/getting-started/) | Configure providers and write your first structured logs |
| [Providers](/modules/logging/providers/) | The eight providers, choosing one, writing your own |
| [Context Enrichment](/modules/logging/context-enrichment/) | Correlation IDs, ambient context, scopes |
| [Privacy & Security](/modules/logging/privacy-security/) | PII/secret redaction, audit trail |
| [Performance](/modules/logging/performance/) | `[LoggerMessage]`, the zero-allocation formatter, rate limiting |
| [Common Mistakes](/modules/logging/common-mistakes/) | The most frequent logging pitfalls |
| [Troubleshooting](/modules/logging/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/logging/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Logging is **MIT-licensed**.
