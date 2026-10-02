# Pragmatic.Documents.Docx.Samples

Runnable samples for [Pragmatic.Documents.Docx](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Documents/samples/Pragmatic.Documents.Docx.Samples/Pragmatic.Documents.Docx.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `PdxTemplateSample` — **the recommended way**: `templates/letter.pdxdoc`, embedded and found by name
  through `IPdxTemplates`, resolved in Italian (`t:` with parameters, `date`/`currency`/`percent` pipes, a
  data-bound list, an `if`), then rendered to DOCX. The samples below build the model in code — the way for
  a table of contents, fields, footnotes and bookmarks, which the markup does not have.
- `BasicDocxSample`
- `LandscapeAndHyperlinkSample`
- `MultiPageWithTocSample`
- `TableAndStyledSample`

## Related

- Module: [Pragmatic.Documents.Docx](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/documents.docx/
- Source: `Pragmatic.Documents.Docx/src/Pragmatic.Documents.Docx/`
