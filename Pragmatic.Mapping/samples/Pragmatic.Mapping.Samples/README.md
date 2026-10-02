# Pragmatic.Mapping.Samples

Runnable samples for [Pragmatic.Mapping](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Mapping/samples/Pragmatic.Mapping.Samples/Pragmatic.Mapping.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `AdvancedAttributesSample`
- `AdvancedNestingSample`
- `ApplyToSample`
- `BasicMappingSample`
- `BidirectionalMappingSample`
- `BodyOnlyAndConstructorSample`
- `CombinedScenariosSample`
- `ConverterCombinationsSample`
- `DeepNestingSample`
- `NestedMappingSample`
- `NullableAndAdvancedSample`
- `ProjectionSample`
- `RealWorldMappingSample`
- `SelectorSample`
- `StructMappingSample`
- `TypeCombinationsSample`
- `TypeConversionSample`

## Related

- Module: [Pragmatic.Mapping](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/mapping/
- Source: `Pragmatic.Mapping/src/Pragmatic.Mapping/`
