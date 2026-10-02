# Pragmatic.Result.Samples

Runnable samples for [Pragmatic.Result](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Result/samples/Pragmatic.Result.Samples/Pragmatic.Result.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `AsyncPipelineSample`
- `BasicResultSample`
- `DeconstructSample`
- `MaybeSample`
- `MultiErrorSample`
- `RailwayOrientedSample`
- `RecoverySample`
- `SideEffectsSample`
- `TryCatchSample`
- `VoidResultSample`

## Related

- Module: [Pragmatic.Result](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/result/
- Source: `Pragmatic.Result/src/Pragmatic.Result/`
