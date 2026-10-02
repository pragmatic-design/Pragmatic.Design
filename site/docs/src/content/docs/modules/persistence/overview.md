---
title: "Pragmatic.Persistence"
description: "Source-generated persistence layer for .NET 10. Declare entities with attributes; the source generator"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/README.md
sidebar:
  order: 0
  label: Overview
---
Source-generated persistence layer for .NET 10. Declare entities with attributes; the source generator
emits repositories, query pipelines, filters, mutations, EF Core configurations, and DI registration,
all visible in `obj/`, fully debuggable, and free of reflection itself. EF Core underneath is not, and
is the framework's stated exception.

## The Problem

Every EF Core project accumulates the same infrastructure per entity: identity properties, audit
fields, soft-delete fields, `Create` factories, typed setters, repository classes, entity
configuration, query filters, DI registration. For one entity with auditing, soft-delete, a
relationship, and a search query, that is ~150 lines of mechanical code. For 30 entities, ~4,500 lines
you write, maintain, and keep in sync, and every new property means touching the entity, the setter,
the factory, the configuration, the DTO mapping, and maybe the query filter.

```csharp
// Without Pragmatic: ~150 lines per entity
public class Order : IEntity, IAuditable, ISoftDelete
{
    public Guid Id { get; set; }
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }       // repeated on every auditable entity
    public string? CreatedBy { get; set; }
    public bool IsDeleted { get; set; }                 // repeated on every soft-deletable entity
    public Guid CustomerId { get; private set; }        // manual FK
    public Customer? Customer { get; set; }             // manual navigation

    public static Order Create(string number, decimal total, Guid customerId) { /* ... */ }
    public void SetTotal(decimal value) => Total = value;
    // + audit/soft-delete fields, Equals, GetHashCode, Repository, EF config, query filter, DI...
}
```

## The Solution

You declare the entity's shape and intent. The generator produces everything else at compile time,
without reflection.

```csharp
// With Pragmatic: 10 lines, everything else is generated
[Entity]
[Auditable]
[SoftDelete]
[BelongsTo<SalesBoundary>]
[Relation.OneToMany<LineItem>]
[Relation.ManyToOne<Customer>]
public partial class Order : IEntity
{
    [LogicKey]
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

From this, the generator emits: `PersistenceId`, `Id`, a `Create()` factory, typed setters, audit +
soft-delete fields, FK + navigation properties, a nested `Repository`, EF Core entity configuration, a
`SoftDeleteFilter`, and DI registration. Rename or add a property and the generated
code follows: no drift, no silently-forgotten mapper.

## Architecture

Three packages form the stack:

| Package | Role | Target |
|---------|------|--------|
| **Pragmatic.Persistence** | Attributes, interfaces, query/filter primitives, mutation contracts | net10.0 |
| **Pragmatic.Persistence.EFCore** | EF Core runtime: DbContext generation, interceptors, bulk ops, query executor | net10.0 |
| **Pragmatic.SourceGenerator** | Unified analyzer that emits all `.g.cs` files | netstandard2.0 |

`Pragmatic.Persistence` is the contract layer (what you declare); `Pragmatic.Persistence.EFCore` is the
runtime; the generator bridges them at compile time. `IRepository<T>` and `IUnitOfWork` live in
`Pragmatic.Abstractions` (Layer 0) so other packages depend on them without the full stack. Deep dive:
[Architecture and Concepts](/modules/persistence/concepts/).

## Installation

```bash
dotnet add package Pragmatic.Persistence
dotnet add package Pragmatic.Persistence.EFCore
dotnet add package Pragmatic.SourceGenerator   # the unified analyzer (ships generated code)
```

Other packages join in when you reference them: the generator detects the reference and emits the
matching code, with nothing else to wire.

| Package | What it adds to an entity's surface |
|---|---|
| `Pragmatic.Actions` | `[DomainAction]`: an operation whose body you write; `[Mutation]`: an operation whose body is derived from the entity's shape. Both get a generated invoker and a boundary-keyed unit of work |
| `Pragmatic.Endpoints` | `[Endpoint]`: the HTTP surface of an action, mutation or query |
| `Pragmatic.Validation` | property attributes (`[Email]`, `[Length]`, `[GreaterThan]`, …) and the validator the invoker runs before persisting |
| `Pragmatic.Caching` | `[Cacheable]` on a query: generated cache key, duration and tag-based invalidation |

Contributors to Pragmatic itself install none of this: inside this repository the packages are
referenced as projects, and every NuGet version is declared once in `Directory.Packages.props`.
[Monorepo Structure](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/howto/monorepo-structure.md) is the map of that layout.

## Quick Start

**1. Declare a boundary**: a marker class naming one slice of the domain. Every entity carrying
`[BelongsTo<CatalogBoundary>]` ends up in the same generated `CatalogDbContext` and behind the same
`IUnitOfWork`, keyed on the marker type: the boundary is the unit of transactional consistency. The
names are derived from the class with the `Boundary` suffix trimmed.

```csharp
[Boundary]                             // from Pragmatic.Actions: see below
public partial class CatalogBoundary;
```

Persistence alone asks for no attribute here: `[BelongsTo<T>]` on the entity is what assigns it.
`[Boundary]` is the Actions one, and it does a second thing: every operation in the namespace and its
sub-namespaces is collected into an `ICatalogActions` interface, with a sub-interface per intermediate
namespace segment (`Catalog.Properties.Mutations` → `ICatalogPropertiesActions`). Past two levels of
nesting that grouping is reported as **PRAG0412**, a warning and not a stop. See
[Boundaries](/modules/persistence/17-boundaries/).

**2. Declare an entity:**

```csharp
[Entity]
[Auditable]
[SoftDelete]
[BelongsTo<CatalogBoundary>]
[Relation.ManyToMany<Amenity>.WithNavigation("Amenities", Inverse = "Properties")]
public partial class Property : IEntity
{
    [Required, LogicKey]
    public string Code { get; private set; } = "";

    [Required]
    public string Name { get; private set; } = "";

    [Range(1, 5)]
    public int StarRating { get; private set; }
}
```

The generator produces `PersistenceId`/`Id`, audit + soft-delete fields, a `Create(...)` factory, typed
`SetXxx()` setters, FK + navigation properties, a nested `Repository : IRepository<Property>`, EF
Core configuration, a `SoftDeleteFilter`, and DI registration.

**3. Write a mutation** (auto-maps to the entity setters; the invoker handles create/validate/persist/events):

```csharp
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/amenities")]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
    public AmenityCategory Category { get; init; }
}
```

**4. Write a query** (the generator builds the LINQ pipeline from `[Filter]`/`[Sort]`; the executor
handles paging, projection, global filters, caching):

```csharp
[Query<Amenity, AmenityDto>]
[Endpoint(HttpVerb.Get, "api/amenities/search")]
public partial class SearchAmenitiesQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NameSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
```

**5. Use the repository:**

```csharp
public sealed class PropertyService(
    IRepository<Property> properties,
    [FromKeyedServices(typeof(CatalogBoundary))] IUnitOfWork uow)
{
    public async Task<Guid> CreateProperty(string code, string name, CancellationToken ct)
    {
        var property = Property.Create(code, name);
        properties.Add(property);
        await uow.SaveChangesAsync(ct);
        return property.PersistenceId;
    }
}
```

Full walkthrough: [Getting Started](/modules/persistence/01-getting-started/).

## Operational note

**`[WithoutFilter<T>]` is a privileged escape hatch.** It bypasses the configured query filters
(tenant, soft-delete, ownership, scope) for the duration of a call. Use it only in privileged contexts
(admin tooling, background jobs, cross-tenant reports), never under a normal request principal. Treat
every call site as an authorization boundary. See [Query Filters](/modules/persistence/07-query-filters/).

## Status

**Stable** within 1.0.0-alpha: the core entity, repository, query and mutation surface is settled; some
advanced areas are still settling. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

## Documentation

### Learn

| Guide | What you will learn |
|-------|---------------------|
| [Architecture and Concepts](/modules/persistence/concepts/) | Mental model, entity lifecycle, decision tree for choosing patterns |
| [Getting Started](/modules/persistence/01-getting-started/) | Activation requirements, partial classes, minimal example |
| [Boundaries](/modules/persistence/17-boundaries/) | How boundaries partition entities, DbContexts, and transaction scopes |

### Entity model

| Guide | What you'll learn |
|-------|-------------------|
| [Entity System](/modules/persistence/02-entity-system/) | `[Entity]`, PersistenceId, `Create()`, setters, `[LogicKey]`, `[PartOf<TParent>]`, identifiers (Guid7, OpaqueId, ShortGuid) |
| [Attributes](/modules/persistence/03-attributes/) | `[Auditable]`, `[Audited]`, `[ValueObject]`, `[SoftDelete]`, `[ConcurrencyAware]`, `[Lookup]`, `[StateMachine]` |
| [Relationships](/modules/persistence/04-relationships/) | `[Relation.*]` attributes, FK generation, cross-boundary rules |
| [State Machine](/modules/persistence/19-state-machine/) | `[StateMachine<T>]` + `[TransitionFrom]`: compile-time guarded status transitions |
| [Advanced Features](/modules/persistence/08-advanced/) | Temporal relations, inheritance, hierarchy, polymorphic, lifecycle, presets, batch |

### Data access

| Guide | What you'll learn |
|-------|-------------------|
| [Repository](/modules/persistence/05-repository/) | `IRepository<T>`, concrete repos, specifications, `IUnitOfWork`, bulk operations |
| [Mutations](/modules/persistence/06-mutations/) | `Mutation<T>`, `ApplyTo()`, collection strategies, modes |
| [Patch](/modules/persistence/13-patch/) | `[Patch<T>]` and its property-set tracking, and why tri-state PATCH needs `[GeneratePatch<T>]` instead |

### Querying

| Guide | What you'll learn |
|-------|-------------------|
| [Query System](/modules/persistence/09-query-system/) | `[Query<T,R>]`, `[Filter]`, `[Sort]`, `[FilterDto<T>]`, `[Join<T>]`, query executor |
| [Query Filters](/modules/persistence/07-query-filters/) | Soft-delete, tenant, permission, visibility filters, toggle, modes |
| [Grid Filtering](/modules/persistence/10-grid-filtering/) | `[GridFilter<T>]`, `[GridAdapter<T>]`, DevExpress/PrimeNG adapters |
| [Projections & Views](/modules/persistence/11-projections-views/) | `[Projectable]`, `[ComputedFilter]`, `[QueryView<T>]`, aggregations |
| [Query Strategy & Loading](/modules/persistence/12-datasource-loading/) | `[QueryStrategy]`, strategies, `[LoadWith<T>]`, split queries |
| [Eager Loading](/modules/persistence/20-eager-loading/) | The navigations derived from a DTO's own shape, and what happens when one is missing |
| [Data Ownership & Visibility](/modules/persistence/18-data-ownership/) | `[HasOwner]`, `[HasAccessScopes]`, data-level authorization |

### Pipelines & diagnostics

| Guide | What you'll learn |
|-------|-------------------|
| [Query Pipeline](/modules/persistence/15-query-pipeline/) | Endpoint → filters → Apply → count/page/project → result |
| [Mutation Pipeline](/modules/persistence/16-mutation-pipeline/) | Endpoint → validation → load/apply → persist → events |
| [Diagnostics](/modules/persistence/14-diagnostics/) | PRAG06xx/07xx diagnostics, root causes, mitigations |
| [Common Mistakes](/modules/persistence/common-mistakes/) | Wrong code → right code for the most common pitfalls |
| [Troubleshooting](/modules/persistence/troubleshooting/) | Checklists for generation, DI, filter, query, and migration issues |

### EF Core implementation

[DbContext generation](/modules/persistence/efcore-01-dbcontext-generation/) ·
[Repository internals](/modules/persistence/efcore-02-repository-implementation/) ·
[Entity configuration](/modules/persistence/efcore-03-entity-configuration/) ·
[Interceptors & runtime](/modules/persistence/efcore-04-interceptors-runtime/) ·
[Bulk operations](/modules/persistence/efcore-05-bulk-operations/) ·
[Filter pipeline](/modules/persistence/efcore-06-filter-pipeline/) ·
[Testing](/modules/persistence/efcore-07-testing-generated-persistence/) ·
[Migration patterns](/modules/persistence/efcore-08-migration-patterns/)

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/persistence/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Persistence is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
