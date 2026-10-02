# Pragmatic.Validation.Samples

Runnable samples for [Pragmatic.Validation](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Validation/samples/Pragmatic.Validation.Samples/Pragmatic.Validation.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `AsyncValidatorSample`
- `BasicValidationSample`
- `ChangeAwareSample`
- `CrossPropertySample`
- `DeepNestedSample`
- `FormatAndDateSample`
- `NestedValidationSample`
- `ValidationErrorApiSample`

## Related

- Module: [Pragmatic.Validation](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/validation/
- Source: `Pragmatic.Validation/src/Pragmatic.Validation/`
