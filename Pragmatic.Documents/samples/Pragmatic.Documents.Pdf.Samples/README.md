# Pragmatic.Documents.Pdf.Samples

Runnable samples for [Pragmatic.Documents.Pdf](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Documents/samples/Pragmatic.Documents.Pdf.Samples/Pragmatic.Documents.Pdf.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `PdxTemplateSample` — **the recommended way**: `templates/receipt.pdxdoc`, embedded and found by name
  through `IPdxTemplates`, resolved in Italian (`t:`, `date`/`currency` pipes, a table, an aggregate,
  `for-each`), then rendered. The samples below build the model in code, for what the markup cannot say.
- `BarcodeAndLandscapeSample`
- `BasicPdfSample`
- `MultiPageSample`
- `TableAndListSample`

## Related

- Module: [Pragmatic.Documents.Pdf](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/documents.pdf/
- Source: `Pragmatic.Documents.Pdf/src/Pragmatic.Documents.Pdf/`
