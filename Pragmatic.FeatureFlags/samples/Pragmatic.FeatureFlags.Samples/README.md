# Pragmatic.FeatureFlags.Samples

Runnable samples for [Pragmatic.FeatureFlags](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## Samples

Each scenario lives in its own `{Scenario}Sample.cs` file, wired into `Program.cs`:

1. `BasicFlagsSample` — defining on/off flags and evaluating them.
2. `PercentageRolloutSample` — deterministic percentage rollout (SHA-256 hash bucketing, no RNG).
3. `TargetingRulesSample` — user / plan / property rules with first-match-wins ordering.
4. `StronglyTypedFlagsSample` — the `IFeatureFlag` pattern (`IsEnabledAsync<TFlag>()`).
5. `ConfigurationStoreSample` — `ConfigurationFeatureFlagStore` bound to `IConfiguration` with runtime reload.
6. `WatchChangesSample` — `WatchAsync` change notifications (the `BackgroundService` pattern).
7. `ContextProviderSample` — `IFeatureFlagContextProvider` ambient context resolution.
8. `CustomStoreSample` — registering a custom `IFeatureFlagStore` via `AddPragmaticFeatureFlags<TStore>()`.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.FeatureFlags/samples/Pragmatic.FeatureFlags.Samples/Pragmatic.FeatureFlags.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Related

- Module: [Pragmatic.FeatureFlags](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/featureflags/
- Source: `Pragmatic.FeatureFlags/src/Pragmatic.FeatureFlags/`
