# Pragmatic.Caching.Samples

Runnable samples for [Pragmatic.Caching](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Caching/samples/Pragmatic.Caching.Samples/Pragmatic.Caching.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `CacheHitMissSample`
- `CacheKeySample`
- `CacheOptionsSample`
- `DurationComparisonSample`
- `InvalidationSample`
- `CategoryRoutingSample` -- registers per-category cache stacks via `CachingBuilder.ForCategory<T>()` and resolves them at runtime through `CacheStackProvider.ForCategory<T>()`; shows that identical logical keys in different categories never collide (distinct prefixes) and that an unregistered category falls back to the default stack.
- `ConditionalCachingSample` -- uses the `CacheFactoryResult<T>`-returning `GetOrSetAsync` overload to let the factory decide per-call whether to cache: `DoNotCache(...)` returns the value without storing it (factory re-runs every call), `Cache(...)` stores it (factory runs once).
- `LiveInvalidationSample` -- seeds a real `ICacheStack`, then invokes the source-generated `ICacheInvalidator.InvalidateAsync()` on `[InvalidatesCache]` event types to remove explicit keys and invalidate tag groups end-to-end.
- `ObservabilitySample` -- subscribes an `ActivityListener` and a `MeterListener` to the `"Pragmatic.Caching"` `ActivitySource`/`Meter` and observes activities and hit/miss/invalidation counters fire on real cache operations.

## Related

- Module: [Pragmatic.Caching](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/caching/
- Source: `Pragmatic.Caching/src/Pragmatic.Caching/`
