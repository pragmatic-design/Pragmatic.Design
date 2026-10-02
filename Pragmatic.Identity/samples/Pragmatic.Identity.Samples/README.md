# Pragmatic.Identity.Samples

Runnable samples for [Pragmatic.Identity](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Identity/samples/Pragmatic.Identity.Samples/Pragmatic.Identity.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `CurrentUserSample` — ICurrentUser singletons, PrincipalKind, property composition
- `IntegrationPatternSample` — DI injection, claim mapping, auth setup (prose)
- `ClaimsAndExtensionsSample` — claims access and extension methods
- `GroupsAndRolesSample` — L2 Groups → Roles → Permissions chain + resource authorization (real DI)
- `LocalIdentityFlowSample` — Identity.Local: Register → Login → ChangePassword → password reset (real `[DomainAction]` classes)
- `JwtTokenSample` — Identity.Local.Jwt: `JwtTokenGenerator`, claim inspection, signing-key guard, rate-limit options
- `AspNetCoreClaimsSample` — Identity.AspNetCore: `ClaimsPrincipalUserAccessor` bridge + `IdentityOptions` claim mapping (+ dev-only header/no-op auth)
- `PersistenceStoresSample` — Identity.Persistence: `EfRolePermissionStore` and `EfGroupRoleStore` over EF Core in-memory, with a grant that activates in the future

> EF Core in-memory is used only to make the persistence stores runnable; a host swaps it for SQL Server / Npgsql.

## Related

- Module: [Pragmatic.Identity](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/identity/
- Source: `Pragmatic.Identity/src/Pragmatic.Identity/`
