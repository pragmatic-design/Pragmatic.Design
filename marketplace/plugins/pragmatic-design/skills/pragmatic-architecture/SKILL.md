---
name: pragmatic-architecture
description: Use when setting up a new Pragmatic.Design project, splitting it into libraries, modules and databases, or weighing a monolith against services (boundary libraries and host, sub-boundaries, multi-tenancy, what to enable).
---

# Pragmatic Architecture

**Covers:** Structure a Pragmatic.Design project before writing code: boundary libraries and host, modules and sub-boundaries, databases, monolith vs distributed, multi-tenancy, what to enable.

This skill is the structuring guide: **how to divide** a line-of-business app into libraries and modules, **what to enable**, and **when**. For attributes and usage patterns, proceed to the `pragmatic-use-*` skills.

## The standard structure

A Pragmatic app has **one or more boundary libraries** (the domain) plus **one host** (the executable process):

```
MyApp/
├── MyApp.slnx
├── NuGet.config
├── src/
│   ├── MyApp.Sales/                    # boundary library: Sales domain
│   │   ├── SalesBoundary.cs            # [Boundary]
│   │   ├── SalesModule.cs              # [Module]
│   │   ├── Orders/                     # sub-boundary: a group of operations
│   │   │   ├── Order.cs                # entity, at the root of the group that writes it
│   │   │   ├── OrderLine.cs            # a child nothing writes on its own lives beside it
│   │   │   ├── Mutations/  Actions/  Queries/
│   │   │   ├── Endpoints/  Dtos/  Validators/  Errors/
│   │   ├── Customers/                  # another sub-boundary
│   │   ├── Events/  Enums/             # what the module publishes: another module handles its
│   │   │                               #   events, its enums travel on the DTOs
│   │   └── Infrastructure/             # the one hat for what it does not publish: Authorization/,
│   │                                   #   Services/, Jobs/, Lifecycle/, EventHandlers/, Endpoints/
│   │                                   #   (route groups)
│   ├── MyApp.Catalog/                  # another boundary library
│   └── MyApp.Host/                     # executable host
│       ├── Program.cs
│       ├── HostModule.cs               # [Module] + [Include<...>]
│       ├── AppDatabase.cs              # [PragmaticDatabase]
│       └── appsettings.json
└── tests/MyApp.Tests/
```

**Rule**: the domain lives in boundary libraries; the host contains no business logic, only composition, database markers, and infrastructure strategy.

## 3-tier config: who decides what

```
Topology (compile-time)  →  Strategy (Program.cs)       →  Business wiring (IStartupStep)
[Module] [Include]          IPragmaticBuilder.Use*()       ConfigureServices/ConfigurePipeline
SG detects and generates    infrastructure choices          domain wiring
```

- **Topology**: what exists (modules, boundaries, databases). Declared with attributes, discovered by the SG.
- **Strategy**: infrastructure choices (DB provider, auth scheme, transport, storage). Goes in `Program.cs` via `Use*()`.
- **Business**: registering domain services, filters, OpenAPI customisation. Goes in `IStartupStep`.

Do not mix tiers: a provider choice does not belong in an `IStartupStep`, and a domain service does not belong in `Program.cs`.

## Boundaries: boundary, module, sub-boundary

- A **boundary** is a cohesive domain unit = one library. Mark it with `[Boundary]` (empty `partial` class) and `[Module(Name = "MyApp.Sales")]`.
- The **namespace is the boundary**: all code under the boundary's namespace flows into it. No explicit assignment attributes are needed.
- **Sub-folders** (`Orders/`, `Customers/`) become **inferred sub-boundaries** from the namespace: the SG generates separate action interfaces (`ISalesOrdersActions`) composed into `ISalesActions`.

### Where a file goes, and why only some placements matter

A folder means two different things depending on where it sits, and telling them apart is what
makes this decidable.

**1. In the namespace of an operation** (an action, mutation, query or endpoint) every segment
before the operation-type segment becomes a **sub-boundary**. It is public API: a caller writes
`boundary.Orders.PlaceOrder(...)`. These segment names are recognised and stripped:

`Mutations · Actions · Queries · Endpoints · Entities · Dtos · Enums · Events · EventHandlers ·
Errors · Services · Validators · Specifications · Filters · Handlers · Processors · Converters ·
Seeding · Lifecycle · FeatureFlags · Infrastructure`

**2. Everywhere else** the folder is only a folder. Entities, DTOs and errors declare **flat**
namespaces (`{Module}.Entities`, `{Module}.Dtos`, `{Module}.Errors`) whatever directory they are
in, so moving one changes no namespace, no generated code and no `using`. Their placement is a
question of reading, and nothing else.

⚠️ `Infrastructure` **is** in the stripped list, and the inference stops at the first recognised
segment, so anything under it is inert whatever the sub-folder is called (`Jobs/` included). It holds
services and wiring, which is its job. An **operation** placed under it gets no group: it lands on the
boundary root, silently. The same holds for an `Endpoints/` at the module root: route groups
(`[EndpointGroup]`) go in `Infrastructure/Endpoints/`, endpoint operations in their resource's folder.

### Naming the group

A sub-boundary is **a group of operations the caller thinks of together**, named after the resource
its routes publish under: `api/sales/orders/...` → `Orders/`.

- **Never name a group after the module.** `MyApp.Sales/Sales/` generates `ISalesSalesActions`, in
  the boundary definition, and the caller writes `boundary.Sales.Sales.Create()`. The module's own
  aggregate belongs at the module root, where its operations become `ISalesActions`.
- **A group must separate something.** One that holds every operation in the module is a level with
  a name and no meaning; split it or drop it.
- **The noun creates the group, not the count.** A lone operation joins an existing noun when one
  fits (a "queue summary" belongs to the candidates whose queue it summarises) and gets its own
  group only when no existing noun owns it.
- **One level.** `PRAG0412` warns past two; `PRAG0413` reports what was inferred.

### Where the entity goes

The generator does not read this, so the rule earns its keep on reading alone. The question is not
how many entities share a folder, but:

> **Does this entity have write operations of its own?**

- **Yes**, it is an aggregate: it sits at the root of the group whose operations write it.
- **No**, it is a child: no folder of its own, beside the aggregate that owns it.

It has a test you can run: `grep "Mutation<TEntity>"`. Nothing found → child. ⚠️ A query alone does
not make an aggregate; a child can be exposed for reading through its parent's group.

So **several entities in one folder is right exactly when they share one aggregate's lifecycle**:
an order with its lines, an amenity with its category and contact details.

## One boundary library or many?

| One boundary | Multiple boundaries |
|---|---|
| Small/cohesive domain, single team | Distinct domains with separate lifecycles |
| MVP, prototype | Multiple teams, separate ownership |
| | Need for independent deployment or scaling (see distributed) |

Guidelines:

- Start with **one boundary** until you have a concrete reason to split. Premature splitting costs more than a late merge.
- Within a boundary, use **sub-boundaries** (folders) to separate features. Maximum ~2 nesting levels; beyond that, it is a signal that a separate boundary is needed.
- Break into a new boundary when: an area has its own team, its own database, or needs to be distributed.
- **Cross-boundary communication**: only via domain event (`pragmatic-use-messaging`) or remote invocation. **Never** direct SQL joins between entities from different boundaries/databases.

## Database

Declare each logical database in the host with a marker:

```csharp
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

- The host attaches modules to databases in its `[Module]`: `[Include<SalesModule, AppDatabase>]`.
- **One database** for most apps. Add a second `[PragmaticDatabase]` only when a boundary has genuine isolation requirements (e.g. separate financial data, compliance, scaling).
- `Provider`: `PostgreSql | SqlServer | SQLite | MySql | InMemory`. Recommended default: PostgreSQL; SQLite for lightweight dev/test.
- Migrations: `app.UsePragmaticMigrations()`, a declarative schema diff with no manual migration files.

## Layer dependencies: the direction of dependencies

```
Layer 0 (Foundation)      Layer 1 (Capabilities)     Layer 2 (Integration)
Abstractions, Result,     Validation, Mapping,       Actions, Endpoints,
Ensure, Identity,         Caching, Temporal,         Persistence.EFCore,
Authorization, Logging    Persistence, Patch         Composition, Messaging
```

Dependencies flow **downward**: a boundary library uses Layer 0–2; the host integrates everything. No dependency that climbs back up the layers. A boundary library does not reference the host.

## Monolith or distributed?

Start **monolithic**: all modules in one host, `[Include<TModule, TDatabase>]`. This is the default for 95% of LOB apps.

Move to **distributed** only with a genuine need for separate deployment/scaling. A remote module is declared in the host with `[RemoteBoundary<BillingModule>]` (instead of `[Include]`; the two are mutually exclusive, **PRAG1685**): the SG generates HTTP invokers and a dispatcher. The URL goes in config (`Pragmatic:RemoteBoundaries:{Module}:BaseUrl`).

Services also talk over a broker (events through the outbox, request/reply, sagas across services), and several instances of one service run behind the gateway with the Agent beside each host: `pragmatic-use-distributed`.

What a `[RemoteBoundary]` does **not** do: invoke a mutation (the HTTP invokers are for actions), resolve its address from discovery (it is configuration), retry or break the circuit on its calls, or speak gRPC. Where a remote mutation is needed, expose an action that performs it, or go over the broker.

## Multi-tenancy

If the app serves multiple tenants, enable tenant resolution in `Program.cs`:

```csharp
app.UseMultiTenancy(mt => mt.UseHeader());     // or UseClaim/UseSubdomain/UseRoute/UseSingleTenant
```

`TenantId` becomes available on `ICurrentUser` and propagates to data filters. Decide early: adding multi-tenancy after the fact touches the schema and the filters.

## What to enable, and when

| Enable | When needed | Do NOT enable if |
|---|---|---|
| `Persistence` + `Persistence.EFCore` | There is persistent state | Stateless/proxy app |
| `Actions` + `Endpoints` | Business operations exposed over HTTP | Library only, no API |
| `Validation` | User input to validate | n/a |
| `Authorization` + `Identity` | Permissions or users | Internal API with no auth (rare) |
| `Messaging` | Cross-boundary reactions or outbox | Everything synchronous within one boundary |
| `Jobs` | Scheduled/recurring work | No background work |
| `Caching` | Expensive queries, stable data | Data that changes on every request |
| `Migrations` | Almost always | Schema managed externally |
| `MultiTenancy` | Multi-tenant app | Single-tenant app |

Principle: **enable by necessity**, not by completeness. Every additional package is surface area to understand and maintain. Adding a module later is easy (the SG detects it on the next build); removing a poorly-chosen one is more costly.

## Recommended starting flow

1. `pragmatic-nuget-feed`: local NuGet feed, if you use previews.
2. `pragmatic-new-app`: scaffold host + first boundary library.
3. `pragmatic-choose-modules`: select packages for your features.
4. Model the domain: `pragmatic-use-persistence`, then `pragmatic-use-actions-endpoints`.
5. Add cross-cutting as needed: `pragmatic-use-authorization`, `-messaging`, `-jobs`, `-caching`, `-logging`.
6. Full cross-cutting patterns catalogue: `../pragmatic-ecosystem/references/patterns-map.md`.
