# Pragmatic.MultiTenancy.Samples

Runnable samples for [Pragmatic.MultiTenancy](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## Sections

1. **TenantScope** — AsyncLocal scoping (begin/end, nesting, async flow).
2-3. Resolution strategy & DI usage reference snippets.
4. **DI wiring** (`DiWiringSample`) — `AddPragmaticMultiTenancy`, every `MultiTenancyBuilder.Use*` strategy, and the `IPragmaticBuilder.UseMultiTenancy()` extension, each building a real container.
5. **HTTP resolvers** (`HttpResolversSample`) — Header / Claim / Subdomain / Route executed against a fabricated `DefaultHttpContext`, including security edge cases (trim, length cap, IP rejection, apex domain).
6. **Middleware** (`MiddlewareSample`) — `TenantResolutionMiddleware` driven directly: resolved path, unresolved pass-through, and the `RequireTenant` → HTTP 400 short-circuit.
7. **Composite resolver** (`CompositeResolverSample`) — chain of responsibility: first-wins, null fall-through, throwing-resolver recovery.
8. **InMemoryTenantStore** (`TenantStoreSample`) — full CRUD lifecycle (seed, create, query, update, deactivate, delete).
9. **MultiTenancyOptions** (`OptionsSample`) — defaults and `services.Configure` → `IOptions<T>`.
10. **DB-per-tenant** (`DbPerTenantSample`) — `TenantDatabaseOptions` safe templating, `TenantConnectionStringProvider` resolution + caching, provisioner selection. Concrete Postgres/SQL Server provisioners are shown as setup-only (need a live server).

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.MultiTenancy/samples/Pragmatic.MultiTenancy.Samples/Pragmatic.MultiTenancy.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Related

- Module: [Pragmatic.MultiTenancy](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/multitenancy/
- Source: `Pragmatic.MultiTenancy/src/Pragmatic.MultiTenancy/`
