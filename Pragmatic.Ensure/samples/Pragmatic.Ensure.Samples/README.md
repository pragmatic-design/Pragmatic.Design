# Pragmatic.Ensure.Samples

Runnable samples for [Pragmatic.Ensure](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Ensure/samples/Pragmatic.Ensure.Samples/Pragmatic.Ensure.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `CollectionAndMiscSample`
- `ConditionalLogicSample`
- `ConstructorGuardsSample`
- `DomainEntitySample`
- `FormatAndNumericSample`

## Related

- Module: [Pragmatic.Ensure](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/ensure/
- Source: `Pragmatic.Ensure/src/Pragmatic.Ensure/`
