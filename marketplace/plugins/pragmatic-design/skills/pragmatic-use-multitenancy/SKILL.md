---
name: pragmatic-use-multitenancy
description: Use when the app is multi-tenant — isolating data per tenant, resolving the tenant from the request, DB-per-tenant, or scoping caches and jobs by tenant — Pragmatic.MultiTenancy, ITenantEntity.
---

# Pragmatic Use Multi-Tenancy

**Covers:** Multi-tenancy with Pragmatic.MultiTenancy from NuGet — mark entities ITenantEntity for automatic tenant assignment + query filtering, an async-safe tenant context, and pluggable resolution (header/claim/subdomain/route/custom) or DB-per-tenant.

Shared-schema by default: mark entities `ITenantEntity` and the generator assigns + filters the tenant
automatically. An `AsyncLocal` tenant context flows through async continuations (including jobs). Miss
no `WHERE TenantId` — the framework adds it.

## When to use

- Per-tenant data isolation in a shared database (or DB-per-tenant).
- Resolving the current tenant from headers/claims/subdomain/route.

## Packages

```xml
<PackageReference Include="Pragmatic.MultiTenancy" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.MultiTenancy.AspNetCore" Version="1.0.0-alpha.*" />  <!-- resolution middleware -->
```

`Pragmatic.MultiTenancy` goes on the boundary library that marks tenant entities,
`Pragmatic.MultiTenancy.AspNetCore` on the host. Neither is in the default host set: an application
without tenants does not reference them.

## Core pattern

Mark the entity — `TenantId` is set on create and filtered on read automatically:

```csharp
[Entity]
public partial class Invoice : IEntity, ITenantEntity
{
    public string TenantId { get; set; } = "";   // you declare it; the interceptor fills it
}
```

## Resolution (host, chosen once)

`UseMultiTenancy` is an extension on **`IPragmaticBuilder`**, not on `WebApplication`: it goes inside
the `PragmaticApp.RunAsync` callback, next to `UseI18N` and `UseAuthorization` — not after a
`builder.Build()`.

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseMultiTenancy(mt => mt.UseHeader());   // or .UseClaim() / .UseSubdomain() / .UseRoute() / custom
    // app.UseMultiTenancy(mt => mt.UseDbPerTenant(...));   // database-per-tenant
}).ConfigureAwait(false);
```

⚠️ **Not optional once anything is `ITenantEntity`.** Both sides are fail-closed, and the default
`MultiTenancyOptions.RequireTenant = true` is what makes them so:

| | with no tenant resolved |
|---|---|
| generated `TenantFilter` (read) | `tenantContext.TenantId != null && …` — hides every row |
| `TenantInterceptor` (write) | throws `TenantNotResolvedException`, naming the entity and the option |

The write fails loudly, so the symptom names the cause; what is left to get right is *choosing* the
tenant where no request sent one (next section). `RequireTenant = false` is the one line for an
application that writes without tenants on purpose.

Two things follow. **Send the tenant in tests too** — an integration test without `X-Tenant-Id` reads
empty, so any assertion of the "nothing is there" kind passes without proving anything. And if lists
are unexpectedly empty, `select count(*)` against the table before suspecting the mapping: that one
query separates *not written* from *written and hidden*, which look identical from the API.

Resolve/read the current tenant via `ITenantContext` (injectable). A `[Cacheable]` query's entry is
prefixed with the tenant (`t:{tenantId}:`) by the query executor, so one tenant's cached page is never
served to another.

**Several strategies chain, in call order.** `mt.UseHeader().UseClaim().UseSingleTenant("demo")` reads
the header, then the claim, and falls back to `demo`; the first non-empty answer wins. One strategy is
registered as itself, several as a `CompositeTenantResolver`. `UseResolver<T>()` is one more link of the
same chain. The application's `UseMultiTenancy(...)` replaces the generator's single-tenant default
rather than joining it: without a fallback of your own, a request no strategy resolves has no tenant.

**A route that belongs to no tenant** — a liveness probe, a public status page — declares it with
`[TenantAgnostic]` (`Pragmatic.Endpoints.Attributes`). `[AllowAnonymous]` is not enough: it lifts
authentication, while the tenant refusal (a **400**) happens in the tenant middleware, before the route
runs and whoever is asking. `[TenantAgnostic]` lifts the *requirement* only: a tenant sent on that route
is still resolved and published.

```csharp
[Endpoint(HttpVerb.Get, "api/status")]
[AllowAnonymous]
[TenantAgnostic]
public partial class StatusEndpoint { … }
```

## Who may claim a tenant

Header, route and subdomain are **client-controlled**: the tenant is whatever the request says. Three
guards decide how far that is trusted, set through `Services.Configure<MultiTenancyOptions>` inside the
same callback:

```csharp
app.UseMultiTenancy(mt =>
{
    mt.UseClaim();                                   // the token says which tenant — the strongest source
    mt.Services.Configure<MultiTenancyOptions>(o =>
    {
        o.EnforceTenantClaim = true;                 // default
        o.EnforceTenantState = true;                 // default
        o.RequireKnownTenant = true;                 // default false — see below
    });
});
```

| Option (default) | What it refuses |
|---|---|
| `EnforceTenantClaim` (true) | an authenticated user whose token carries a tenant claim (`TenantClaimType`, `tenant_id`) asking for another tenant by header/route/subdomain — rejected, neither value used. ⚠️ A token **without** the claim is not checked: issue the claim, or a signed-in user of A can send `X-Tenant-Id: B` |
| `EnforceTenantState` (true) | a tenant the store knows in any state but `Active` — 403. Only with an `ITenantStore` that knows the tenant |
| `RequireKnownTenant` (false) | a tenant the store does not know — 404. Off by default because the generated host registers an **empty** `InMemoryTenantStore`: on, with nothing seeded, it refuses every request. Without it, an invented id becomes the request's tenant and writes land under a tenant nobody onboarded |
| `RequireTenant` (true) | no tenant at all (above) |

Names: `TenantHeaderName` (`X-Tenant-Id`), `TenantClaimType` (`tenant_id`), `TenantRouteParameter`
(`tenantId`) — or pass them to `UseHeader(name)`, `UseClaim(type)`, `UseRoute(name)`.

**The register of tenants** is `ITenantStore` (`TenantInfo`: `TenantId`, `TenantName`, `State`,
`ConnectionString`, `Metadata`). The framework ships only `InMemoryTenantStore` (`Seed(...)` at start-up);
a register in the database is a class of yours implementing the interface, registered as a singleton.
Behind the gateway, `X-Tenant-Id` from the client is stripped and recomputed from the token
(`pragmatic-use-distributed`).

## Database per tenant

`Pragmatic.MultiTenancy.Persistence`: `mt.UseDbPerTenant(db => { db.DefaultConnectionString = …;
db.ConnectionStringTemplate = "…Database=tenant_{0}"; })`. A tenant whose `TenantInfo.ConnectionString`
is set gets its own database — an interceptor rewrites the connection on open, no DbContext change — and
the others stay on the shared one with row filtering; both kinds can coexist.

⚠️ **Nothing creates the database.** Provisioning is `ITenantDatabaseProvisioner` (Postgres and SQL
Server ship; register one with `mt.Services.UseAutoProvision<T>()`), and **the application calls it** at onboarding, then
runs the migrations for that database — the target schema is generated into the host, so the framework
cannot compose the two for you. A tenant pointed at a database that does not exist fails on its first
request with the driver's error.

## Outside a request there is no tenant

A background job, a seeder or a CLI runs with nothing resolved, so the fail-closed read above applies
in full: the query returns **zero rows and reports success**. Open the scope explicitly —
`using var scope = TenantScope.BeginScope(tenantId);` — and every query inside it, Pragmatic filter and
EF Core query filter alike, sees that tenant, because the registered `ITenantContext` is
`AmbientTenantContext` (request first, scope as the fallback). To read *across* tenants, lift the rule
with `FilterMode.Background` through `IQueryFilterToggle`; it keeps soft-delete. `TenantScope` ships in
`Pragmatic.Abstractions` under the `Pragmatic.MultiTenancy` namespace, so a domain library that has
only the abstractions can open one. Full treatment, including per-tenant recurring jobs:
`pragmatic-use-jobs`.

⚠️ **The scope has to be around the save, and one context cannot serve two tenants.** A sweep that
reads across tenants with `FilterMode.Background`, opens a `TenantScope` per row to change it, and then
calls `SaveChangesAsync` after the loop writes with **nothing resolved** — a
`TenantNotResolvedException`. And moving the save
inside the scope is not enough either: the connection is chosen when the context opens it, so a
per-tenant write needs a **container scope** per tenant (`IServiceScopeFactory.CreateScope()` inside
`TenantScope.BeginScope(tenantId)`), which gives a context, a repository and a unit of work of that
tenant's own. Shape:

```csharp
foreach (var tenantId in overdue.Select(row => row.TenantId).Distinct())
{
    using var tenant = TenantScope.BeginScope(tenantId);
    using var scope = scopes.CreateScope();          // IServiceScopeFactory

    var owned = scope.ServiceProvider.GetRequiredService<IRepository<Case>>();
    var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(IntakeBoundary));
    // read again — the ordinary tenant-filtered read — change, then save inside this scope
}
```

## The signal that isolation works

`Pragmatic.Testing.SourceGenerator`, running in the **test** project, emits
`_ContractTests.{Boundary}.Crud.g.cs` with a `{Boundary}CrudContractTests` class, and for every entity
that implements `ITenantEntity` it adds `Create{Operation}_IsNotVisibleToAnotherTenant`, named after the
create operation (`CreateDraftInvoiceMutation` → `CreateCreateDraftInvoiceMutation_…`). That test is the
one that fails when resolution is misconfigured — don't hand-write it, and don't consider isolation
verified without it.

⚠️ **It may ask you for a body.** The generator fills the create's body from the operation's shape, and
a required member it cannot invent — a foreign key, or a nested collection of another mutation, which
is how an aggregate that carries its children is written — leaves it with none. The test is emitted
anyway and fails with a message naming the create: answer it from `PragmaticContractHost.BodyFor` in
the collection's fixture, where the row it needs can be seeded first.

⚠️ **And the isolation test reads the `Location` the create answers.** A create that declares
`[CreatedAt]` and has no single read behind that route answers 405 there, and the isolation proof
cannot be made.

> Licensing: `Pragmatic.MultiTenancy` is PolyForm Small Business (free for small businesses).

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing and Casework example
applications — code that compiles and that `Invoicing.IntegrationTests` and `Casework.IntegrationTests`
exercise — and kept identical to it by the gate: a tenant entity, the tenant from the token with the
guards written out, a register of tenants over the application's table, work with no request done tenant
by tenant, a database per tenant beside the shared schema, and the refusals the guards make.
