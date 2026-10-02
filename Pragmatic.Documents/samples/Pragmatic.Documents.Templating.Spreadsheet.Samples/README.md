# Pragmatic.Documents.Templating.Spreadsheet.Samples

Runnable samples for [Pragmatic.Documents.Templating.Spreadsheet](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Documents/samples/Pragmatic.Documents.Templating.Spreadsheet.Samples/Pragmatic.Documents.Templating.Spreadsheet.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `CsvFileDataSourceSample` — a spreadsheet the operator puts beside the application
- `CsvStreamDataSourceSample` — one a tenant uploaded, which has no path
- `InMemorySpreadsheetSample`
- `XlsxFileDataSourceSample`

## Related

- Module: [Pragmatic.Documents.Templating.Spreadsheet](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/documents.templating.spreadsheet/
- Source: `Pragmatic.Documents.Templating.Spreadsheet/src/Pragmatic.Documents.Templating.Spreadsheet/`
