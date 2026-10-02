# Pragmatic.Events.Samples

Runnable samples for [Pragmatic.Events](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Events/samples/Pragmatic.Events.Samples/Pragmatic.Events.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `BasicDispatchSample`
- `EfCorePatternSample`
- `ErrorResilienceSample`
- `HandlerOrderingSample`
- `MultiEventSample`
- `RegistrationSample`
- `TimeProviderSample`
- `OutboxSample` — transactional outbox: events captured in-transaction, delivered asynchronously (runs against in-memory SQLite)

## Related

- Module: [Pragmatic.Events](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/events/
- Source: `Pragmatic.Events/src/Pragmatic.Events/`
