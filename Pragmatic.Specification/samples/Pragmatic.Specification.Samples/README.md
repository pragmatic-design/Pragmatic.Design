# Pragmatic.Specification.Samples

Runnable samples for [Pragmatic.Specification](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Specification/samples/Pragmatic.Specification.Samples/Pragmatic.Specification.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Related

- Module: [Pragmatic.Specification](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/specification/
- Source: `Pragmatic.Specification/src/Pragmatic.Specification/`
