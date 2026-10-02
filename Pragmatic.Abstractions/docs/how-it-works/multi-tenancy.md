# Multi-Tenancy — whose data this is

> **Scope**: `src/Pragmatic.Abstractions/MultiTenancy/` — 12 files.
> `ITenantContext` · `IMutableTenantContext` · `ITenantEntity` · `ITenantResolver` · `ITenantStore` ·
> `ITenantLifecycleObserver` · `ObservedTenantStore` · `TenantAgnosticEndpoint` · `TenantInfo` ·
> `TenantScope` · `TenantState` · `UnresolvedTenantContext`
>
> **Not covered here**: the resolution strategies (header, claim, route, subdomain) and the
> middleware that runs them live in `Pragmatic.MultiTenancy.AspNetCore`; the interceptor and the
> generated query filters live in `Pragmatic.Persistence.EFCore` and in the generator. Both are
> named where they matter, not opened. The user's own tenant claim is a different thing — see
> [identity](identity.md).

For the member-by-member catalogue, see [interfaces](../interfaces.md#8-multi-tenancy). This
document is about how the pieces fit together, and why the same invariant is enforced three times.

## Two questions, not one

Multi-tenancy in Abstractions is two separable concerns that a single interface would have fused:

- **Who is the current tenant** — `ITenantContext`, a scoped ambient value, resolved per request
  before anything touches data.
- **Which rows belong to a tenant** — `ITenantEntity`, a marker on the entity carrying a
  `TenantId` column.

Keeping them apart is what lets a background job, which has no request and no ambient tenant, still
write rows for a specific tenant: it sets the context explicitly, and every mechanism downstream
behaves as if a request had arrived.

`ITenantContext` is read-only on purpose. `IsResolved` exists so consumers can tell "tenant A" from
"no tenant at all" without inspecting a string for emptiness — a distinction the interceptor
depends on.

## `IMutableTenantContext` — the non-HTTP entry points

Everything that resumes work outside a request needs to restore the tenant that owned it.
`SetTenant` returns an `IDisposable` that puts the previous value back on dispose, so the pattern
is a `using` block and the restore cannot be forgotten on an exception path.

This is not a hypothetical: it is how four deferred paths in the framework work. The message outbox
and transport subscriptions (`Messaging`), the job processor (`Jobs`) and the notification delivery
worker (`Notifications`) each read the tenant stored on the row they are about to process, set it,
and do the work inside that scope.

**Resolve `IMutableTenantContext` directly** to do this — do not resolve `ITenantContext` and cast.
The registered `ITenantContext` is a read-only composite over the request-scoped context and the
ambient scope, so the cast yields null and the tenant is silently never set. That is the whole
failure mode: nothing throws, the scope is entered with no tenant, and a fail-closed query filter
then matches no rows.

The point that makes it correct: it is the **same** context object the query filters read. Restoring
the tenant on a background thread does not merely tag the new rows — it also scopes every read that
handler performs, through the mechanisms below.

## Three mechanisms, one invariant

A tenant leak is the kind of defect you get to have once. The framework therefore enforces
isolation at three independent points, and no single one of them is load-bearing on its own.

**1. The runtime query filter.** For every entity implementing `ITenantEntity`, the generator emits
a `TenantFilter` — a `sealed class` nested inside the entity's own partial class, implementing
`IQueryFilter<TEntity>` and `ITenantFilter`, taking `ITenantContext` by constructor and running at
**priority 200** in the query pipeline. Anything that goes through a repository or a generated
query is filtered here.

**2. The EF global query filter.** When a boundary contains tenant entities, the generated
`BoundaryDbContext` takes an optional `ITenantContext` and registers a named query filter,
`"Tenant"`, on each of them, plus twin filters on the ad-hoc saga and batch tables. This is the
defence-in-depth complement: it also covers a raw `Set<T>()` that bypasses the query pipeline
entirely. Both filters are **fail-closed** — with no tenant resolved, the result is no rows, never
all rows.

**3. Exclusion from generated patches.** `PatchTransform` lists `ITenantEntity` among the excluded
interfaces, so `TenantId` never appears in a generated patch type. Patching it would reassign a row
to another tenant through a route that looks like an ordinary field update. The exclusion is keyed
on the **interface**, not on the property name, so a domain property that merely happens to be
called `TenantId` on a non-tenant entity stays patchable.

To which the write path adds `TenantInterceptor` in `Pragmatic.Persistence.EFCore`:

| On | Behaviour |
|---|---|
| Insert, tenant resolved | `TenantId` is stamped from the ambient context. Application code never assigns it. |
| Insert, no tenant resolved | Entities are left untouched — this is the seeding, migration and CLI path, and it is not an error. |
| Update | A modified `TenantId` is reverted to the loaded value. Tenant transfer through an update is rejected, not merely discouraged. |

## The tenant catalogue

`ITenantStore` is the list of tenants that exist: lookup by id, enumeration, registration. The
generated host registers `InMemoryTenantStore` as a singleton when multi-tenancy is detected, so a
small fixed set of tenants needs no wiring at all. Applications that keep their tenants elsewhere
supply their own implementation.

`DeleteAsync` **has a default implementation that throws `NotSupportedException`**. That shape is
chosen over an optional member or a silent no-op: an append-only store stays source-compatible when
the member is added, and an application that calls it finds out loudly rather than believing a
tenant was removed. `InMemoryTenantStore` and the Agent client's tenant store override it.

## The lifecycle emits a signal

Anything derived from the **set** of tenants and held in memory has to be told when that set changes.
`ITenantLifecycleObserver` is that signal: `OnCreatedAsync`, `OnUpdatedAsync`, `OnDeactivatedAsync`,
`OnDeletedAsync`. Register implementations in DI and they are all told, in order, before the store's
write returns — a caller that creates a tenant and immediately serves a request for it does not race
the refresh. An observer that throws is logged and skipped: the write already happened, and failing
the caller would report it as one that did not.

The signal is raised by `ObservedTenantStore`, a decorator, and not by the stores themselves. There
are three store implementations in the framework and a fourth in every application that writes its
own; wrapping the registration makes the signal true for stores that do not know the type exists. The
generated host wraps the default store, and `UseAgent()` wraps the one it substitutes — anything that
replaces the `ITenantStore` registration has to wrap its own, or the signal stops there.

⚠️ It sees only writes that go **through** `ITenantStore`. A database-backed store whose table is also
written by a migration or another process announces nothing, and a consumer that must survive that
needs its own reconciliation.

The first consumer is the `[Lookup]` preload: `LookupCacheTenantObserver` loads the lookup caches for
a tenant that appears after startup and drops them for one that is deactivated or deleted. It is
registered only when a `[Lookup]` entity is tenant-scoped — a lookup with one cache for the whole
process has nothing to follow.

`TenantInfo` carries the tenant's identity, state, and — for database-per-tenant deployments — its
connection string. It is a `record`, and it overrides `PrintMembers` to print
`ConnectionString = ***`. A record's generated `ToString()` is exactly the thing that ends up in a
log line or an exception message by accident; masking it there costs one method and closes the
whole class of leak.

`TenantState` has **pinned numeric values** (`Active = 0`, `Migrating = 1`, `Suspended = 2`,
`Deactivated = 3`, `Provisioning = 4`). The ordering reads oddly — `Provisioning` comes first in a
tenant's life and last in the enum — and that is the evidence that the values are pinned for a
reason: these integers are persisted, and renumbering them to make the enum read nicely would
silently reinterpret every stored row.

## Resolution, and where to plug in

`ITenantResolver` is the strategy that turns an incoming request into a tenant id.
`Pragmatic.MultiTenancy.AspNetCore` ships resolvers for header, claim, route and subdomain, plus a
composite that tries several in order, and applications select them through the module's builder:

```csharp
app.UseMultiTenancy(mt => mt.UseHeader());
```

To resolve tenants some other way — a lookup table, a certificate, a gRPC metadata entry — implement
`ITenantResolver` and register it with the same builder; the middleware and everything downstream
are unchanged.

`UnresolvedTenantContext.Instance` is an immutable singleton answering "no tenant, not resolved" to
everything. Code generated by the framework resolves `ITenantContext` optionally, so a
single-tenant application needs nothing at all; an application that would rather never handle a
`null` context can register the singleton as the fallback:

```csharp
services.AddSingleton<ITenantContext>(UnresolvedTenantContext.Instance);
```

## Tenant from the context, tenant from the user

`ICurrentUser` also exposes a `TenantId`, and the two are not interchangeable. The identity one is
what the caller's token *claims*; `ITenantContext` is what the request was *resolved to*. An
application that needs both reads each from its own source — the Showcase feature-flag context
provider takes `TenantId` from `ITenantContext` and `UserId` from `ICurrentUser`.

## External references

Named here, described where they live:

- **`Pragmatic.MultiTenancy` / `.AspNetCore`** — `MutableTenantContext`, the four resolvers, the
  composite resolver, `TenantResolutionMiddleware`, `InMemoryTenantStore`, and the
  `UseMultiTenancy(...)` builder.
- **`Pragmatic.MultiTenancy.Persistence` → `TenantConnectionStringProvider`** — resolves the
  per-tenant connection string from `ITenantStore` for database-per-tenant deployments.
- **`Pragmatic.Persistence.EFCore` → `TenantInterceptor`** — stamps and protects `TenantId` on save.
- **`Pragmatic.Configuration` → `ConfigurationResolver`** — reads `ITenantContext` to apply
  per-tenant configuration overrides.
- **`Pragmatic.Migrations` → `TenantMigrationOrchestrator`** — enumerates `ITenantStore` to run
  migrations across every tenant database.
- **`Pragmatic.Jobs`, `Pragmatic.Messaging`, `Pragmatic.Events.EFCore`, `Pragmatic.Notifications`** —
  the `IMutableTenantContext` callers listed above.
