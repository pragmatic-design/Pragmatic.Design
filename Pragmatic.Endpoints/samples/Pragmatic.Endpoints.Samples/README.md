# Pragmatic.Endpoints.Samples

Runnable samples for [Pragmatic.Endpoints](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

WebApplication (minimal API). Starts an HTTP server on a local port. Send requests via the provided `.http` file or `curl`.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Endpoints/samples/Pragmatic.Endpoints.Samples/Pragmatic.Endpoints.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Related

- Module: [Pragmatic.Endpoints](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/endpoints/
- Source: `Pragmatic.Endpoints/src/Pragmatic.Endpoints/`
