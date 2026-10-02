---
title: "Pragmatic.MultiTenancy"
description: "Shared-schema multi-tenancy for the Pragmatic.Design ecosystem: tenant resolution, scoping, and"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.MultiTenancy/README.md
sidebar:
  order: 0
  label: Overview
---
Shared-schema multi-tenancy for the Pragmatic.Design ecosystem: tenant resolution, scoping, and
automatic query filtering: zero config for single-tenant apps, pluggable strategies for SaaS.

## The Problem

Multi-tenant SaaS must isolate data per tenant. Without framework support, every query must filter by
tenant, every write must assign it, and every background job must propagate the tenant context. Miss
one `WHERE TenantId = @current` and you have a cross-tenant data leak, something code review catches
sometimes and tests catch never.

```csharp
// Without Pragmatic: manual tenant filtering everywhere (easy to forget → data leak)
return await db.Invoices.Where(i => i.TenantId == currentTenantId).ToListAsync(ct);
```

Caching leaks too (`invoices:latest` returns the wrong tenant's data), and resolution varies (headers
in dev, JWT in prod, subdomains for some APIs).

## The Solution

Mark entities `ITenantEntity`; the generator filters and assigns the tenant automatically, and an
`AsyncLocal` tenant context flows through async continuations (including background jobs).

```csharp
[Entity]
public partial class Invoice : IEntity, ITenantEntity { /* TenantId set + filtered automatically */ }
```

```csharp
// resolution strategy chosen once, in Program.cs
app.UseMultiTenancy(mt => mt.UseHeader());   // or UseClaim / UseSubdomain / UseRoute / UseResolver<T>
                                              // UseDbPerTenant(...) for DB-per-tenant (Persistence package)
```

> **Security.** `header` / `route` / `subdomain` read the tenant from client-controlled input and are meant for **pre-auth** resolution. For authenticated requests the resolved tenant is validated against the user's `tenant_id` claim (fail-closed, HTTP 403 on mismatch): an authenticated user cannot escalate to another tenant by spoofing the header/route/host. See [tenant-resolution.md](/modules/multi-tenancy/tenant-resolution/#claim-guard-cross-tenant-protection).

## Installation

```bash
dotnet add package Pragmatic.MultiTenancy.AspNetCore   # HTTP resolvers + UseMultiTenancy
dotnet add package Pragmatic.MultiTenancy.Persistence  # optional: DB-per-tenant
```

| Package | Role |
|---------|------|
| `Pragmatic.MultiTenancy` | Tenant context, scoping, configuration; no ASP.NET Core dependency |
| `Pragmatic.MultiTenancy.AspNetCore` | Header/claim/subdomain/route resolvers, the resolution middleware, `UseMultiTenancy` |
| `Pragmatic.MultiTenancy.Persistence` | DB-per-tenant: per-tenant connection strings, provisioning, migration orchestration |

`ITenantEntity` lives in `Pragmatic.Abstractions`, so a domain module marks its entities without
referencing any of these.

## Status

**Functional** within 1.0.0-alpha: tenant resolution strategies, shared-schema filtering,
database-per-tenant, and non-HTTP propagation. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/multi-tenancy/concepts/) | The tenancy model, `ITenantEntity`, ambient context, cache scoping |
| [Getting Started](/modules/multi-tenancy/getting-started/) | Mark entities, choose a resolver, wire the middleware |
| [Tenant Resolution](/modules/multi-tenancy/tenant-resolution/) | Header/claim/subdomain/route/custom resolvers, ordering, non-HTTP contexts |
| [Common Mistakes](/modules/multi-tenancy/common-mistakes/) | The most frequent tenancy pitfalls |
| [Troubleshooting](/modules/multi-tenancy/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/multi-tenancy/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.MultiTenancy is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
