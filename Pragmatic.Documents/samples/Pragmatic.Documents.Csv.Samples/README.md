# Pragmatic.Documents.Csv.Samples

Runnable samples for [Pragmatic.Documents.Csv](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Documents/samples/Pragmatic.Documents.Csv.Samples/Pragmatic.Documents.Csv.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `BasicRoundTripSample`
- `FormulaInjectionSample`
- `LocaleSample`
- `QuotingAndEdgeCasesSample`

## Related

- Module: [Pragmatic.Documents.Csv](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/documents.csv/
- Source: `Pragmatic.Documents.Csv/src/Pragmatic.Documents.Csv/`
