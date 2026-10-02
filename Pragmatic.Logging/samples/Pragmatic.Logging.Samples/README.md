# Pragmatic.Logging.Samples

Runnable samples for [Pragmatic.Logging](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Logging/samples/Pragmatic.Logging.Samples/Pragmatic.Logging.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `BasicLoggingSample` — minimal setup, log levels, exceptions
- `StructuredLoggingSample` — template parameters + `[LoggerMessage]` source generator
- `ScopesSample` — ambient `BeginScope` properties
- `RedactionSample` — filters + sensitive-data redaction
- `MultipleProvidersSample` — fan-out to multiple sinks
- `ContextManagerSample` — `IContextManager` aggregation + `LogContextScope` (AsyncLocal)
- `CorrelationIdSample` — `CorrelationIdProvider` (in-memory `DefaultHttpContext`, header-injection rejection)
- `MemoryProviderSample` — in-memory capturing sink, queried by level/category/text
- `FileProvidersSample` — persistent `AddFile` + `AddJson` sinks, read back from disk
- `RateLimitingSample` — `HighPerformanceRateLimiter` TokenBucket / SlidingWindow / FixedWindow
- `CompliancePresetSample` — `UseCompliancePreset(GDPR)` + `EnableDataRedaction` + `EnableAuditTrail`, and `UseHighPerformancePreset`
- `BootstrapLoggerSample` — pre-DI startup logging with transition to the full pipeline
- `AuditTrailSample` — `MemoryAuditStorage` + `AuditQueryBuilder` + statistics + retention

> Note: the Pragmatic `[LoggerMethod]` / `[GeneratePropertyAccessor]` source-generator
> attributes are not yet wired to an active generator, so they are intentionally not sampled.
> The working source-generated logging path is shown via Microsoft `[LoggerMessage]` in
> `StructuredLoggingSample`.

## Related

- Module: [Pragmatic.Logging](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/logging/
- Source: `Pragmatic.Logging/src/Pragmatic.Logging/`
