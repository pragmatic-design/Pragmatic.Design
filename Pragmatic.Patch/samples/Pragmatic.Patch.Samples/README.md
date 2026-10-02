# Pragmatic.Patch.Samples

Runnable samples for [Pragmatic.Patch](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Patch/samples/Pragmatic.Patch.Samples/Pragmatic.Patch.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `EndpointPatternSample`
- `JsonDeserializationSample`
- `OptionalApiSample`
- `PatchApplySample`
- `PrivateSetterSample`

## Related

- Module: [Pragmatic.Patch](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/patch/
- Source: `Pragmatic.Patch/src/Pragmatic.Patch/`
