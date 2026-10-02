# Pragmatic.Documents.Email.Samples

Runnable samples for [Pragmatic.Documents.Email](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Documents/samples/Pragmatic.Documents.Email.Samples/Pragmatic.Documents.Email.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `FooterAndSocialSample`
- `HeroAndColumnsSample`
- `MarkupEmailSample` — **the recommended way**: `templates/invoice-paid.pdxemail`, embedded and found by
  name through `IPdxTemplates`, in Italian and English — subject, preheader, HTML and plain text from one
  file, the header an imported partial (`mail-header.pdxemail`)
- `SimpleEmailSample`

## Related

- Module: [Pragmatic.Documents.Email](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/documents.email/
- Source: `Pragmatic.Documents.Email/src/Pragmatic.Documents.Email/`
