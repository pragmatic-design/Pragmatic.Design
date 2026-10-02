# Pragmatic.Documents.Xlsx.Samples

Runnable samples for [Pragmatic.Documents.Xlsx](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Documents/samples/Pragmatic.Documents.Xlsx.Samples/Pragmatic.Documents.Xlsx.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `BasicSpreadsheetSample`
- `FormulasAndFreezeSample`
- `MultipleSheetsSample`
- `StylingAndColumnsSample`

## Related

- Module: [Pragmatic.Documents.Xlsx](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/documents.xlsx/
- Source: `Pragmatic.Documents.Xlsx/src/Pragmatic.Documents.Xlsx/`
