# Pragmatic.Authorization.Samples

Runnable samples for [Pragmatic.Authorization](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Authorization/samples/Pragmatic.Authorization.Samples/Pragmatic.Authorization.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `PolicyEvaluationSample`
- `ResourcePolicySample`
- `RolePermissionSample`
- `WildcardMatcherSample`
- `AsyncResourcePolicySample` — async (I/O-bound) policies, `RequireExternalPermission`, async `& | !` composition
- `PolicySerializationSample` — `PolicySerializer` round-trip: `ResourcePolicy` <-> `PolicyExpression` <-> JSON
- `CacheInvalidatorSample` — `IPermissionCacheInvalidator` per-user / per-tenant invalidation
- `CustomPermissionProviderSample` — custom `IPermissionProvider` composed via `CompositePermissionProvider`
- `ManagementSample` — runtime RBAC flow (Management package; in-memory analog + EF action API notes)

## Related

- Module: [Pragmatic.Authorization](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/authorization/
- Source: `Pragmatic.Authorization/src/Pragmatic.Authorization/`
