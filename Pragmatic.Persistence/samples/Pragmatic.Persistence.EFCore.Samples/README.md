# Pragmatic.Persistence.EFCore.Samples

Runnable samples for [Pragmatic.Persistence.EFCore](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Persistence.EFCore/samples/Pragmatic.Persistence.EFCore.Samples/Pragmatic.Persistence.EFCore.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `BulkOperationsSample`
- `EntityCrudSample`
- `IncludeHintsSample`
- `MutationConvertersSample`
- `PatchSample`
- `ProjectableSample`
- `QueryExecutorSample`
- `SoftDeleteAuditSample`

## Related

- Module: [Pragmatic.Persistence.EFCore](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/persistence.efcore/
- Source: `Pragmatic.Persistence.EFCore/src/Pragmatic.Persistence.EFCore/`
