# Pragmatic.Documents.Markup.Samples

Runnable samples for [Pragmatic.Documents.Markup](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Documents/samples/Pragmatic.Documents.Markup.Samples/Pragmatic.Documents.Markup.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

The templates are files in `templates/`, embedded in the assembly and found by name through
`IPdxTemplates` — the way an application keeps them. `SampleTemplates` is the one-time wiring
(`AddPdxTemplates` and the translations), which a Pragmatic host generates.

- `SimpleMarkupSample` — `hello.pdxdoc`, static text
- `DataBoundMarkupSample` — `invoice.pdxdoc` in Italian and English: an imported partial
  (`letterhead.pdxdoc`), `t:` translations with parameters, `date`/`currency` pipes, a table over a
  collection, an aggregate (`items.sum(amount)`), an `if`
- `BatchPageSample` — `payslips.pdxdoc`, one page per item of `page-data-source`

## Related

- Module: [Pragmatic.Documents.Markup](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/documents.markup/
- Source: `Pragmatic.Documents.Markup/src/Pragmatic.Documents.Markup/`
