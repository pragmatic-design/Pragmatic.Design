# Pragmatic.Resilience.Samples

Runnable samples for [Pragmatic.Resilience](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Resilience/samples/Pragmatic.Resilience.Samples/Pragmatic.Resilience.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `CircuitBreakerDemoSample`
- `ErrorTypesSample`
- `FallbackDemoSample`
- `FluentBuilderSample`
- `HedgingDemoSample`
- `NamedPoliciesSample`
- `RateLimiterDemoSample`
- `ResultBridgeSample`
- `RetryDemoSample`
- `SourceGeneratorSample`
- `TimeoutDemoSample`

## Related

- Module: [Pragmatic.Resilience](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/resilience/
- Source: `Pragmatic.Resilience/src/Pragmatic.Resilience/`
