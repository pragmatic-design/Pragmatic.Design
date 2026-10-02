# Pragmatic.Persistence

Source-generated persistence layer for .NET 10. Declare entities with attributes; the source generator
emits repositories, query pipelines, filters, mutations, EF Core configurations, and DI registration —
all visible in `obj/`, fully debuggable, and free of reflection itself. EF Core underneath is not, and
is the framework's stated exception.

## The Problem

Every EF Core project accumulates the same infrastructure per entity: identity properties, audit
fields, soft-delete fields, `Create` factories, typed setters, repository classes, entity
configuration, query filters, DI registration. For one entity with auditing, soft-delete, a
relationship, and a search query, that is ~150 lines of mechanical code. For 30 entities, ~4,500 lines
you write, maintain, and keep in sync — and every new property means touching the entity, the setter,
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
code follows — no drift, no silently-forgotten mapper.

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
[Architecture and Concepts](docs/concepts.md).

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
| `Pragmatic.Actions` | `[DomainAction]` — an operation whose body you write; `[Mutation]` — an operation whose body is derived from the entity's shape. Both get a generated invoker and a boundary-keyed unit of work |
| `Pragmatic.Endpoints` | `[Endpoint]` — the HTTP surface of an action, mutation or query |
| `Pragmatic.Validation` | property attributes (`[Email]`, `[Length]`, `[GreaterThan]`, …) and the validator the invoker runs before persisting |
| `Pragmatic.Caching` | `[Cacheable]` on a query — generated cache key, duration and tag-based invalidation |

Contributors to Pragmatic itself install none of this: inside this repository the packages are
referenced as projects, and every NuGet version is declared once in `Directory.Packages.props`.
[Monorepo Structure](../docs/howto/monorepo-structure.md) is the map of that layout.

## Quick Start

**1. Declare a boundary** — a marker class naming one slice of the domain. Every entity carrying
`[BelongsTo<CatalogBoundary>]` ends up in the same generated `CatalogDbContext` and behind the same
`IUnitOfWork`, keyed on the marker type: the boundary is the unit of transactional consistency. The
names are derived from the class with the `Boundary` suffix trimmed.

```csharp
[Boundary]                             // from Pragmatic.Actions — see below
public partial class CatalogBoundary;
```

Persistence alone asks for no attribute here: `[BelongsTo<T>]` on the entity is what assigns it.
`[Boundary]` is the Actions one, and it does a second thing — every operation in the namespace and its
sub-namespaces is collected into an `ICatalogActions` interface, with a sub-interface per intermediate
namespace segment (`Catalog.Properties.Mutations` → `ICatalogPropertiesActions`). Past two levels of
nesting that grouping is reported as **PRAG0412**, a warning and not a stop. See
[Boundaries](docs/17-boundaries.md).

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

Full walkthrough: [Getting Started](docs/01-getting-started.md).

## Operational note

**`[WithoutFilter<T>]` is a privileged escape hatch.** It bypasses the configured query filters
(tenant, soft-delete, ownership, scope) for the duration of a call. Use it only in privileged contexts
— admin tooling, background jobs, cross-tenant reports — never under a normal request principal. Treat
every call site as an authorization boundary. See [Query Filters](docs/07-query-filters.md).

## Status

Core entity/repository/query/mutation surface is stable within 1.0.0-alpha; some advanced areas are
still settling. See the [roadmap](../docs/ROADMAP.md) for what is moving before 1.0.

## Documentation

### Learn

| Guide | What you will learn |
|-------|---------------------|
| [Architecture and Concepts](docs/concepts.md) | Mental model, entity lifecycle, decision tree for choosing patterns |
| [Getting Started](docs/01-getting-started.md) | Activation requirements, partial classes, minimal example |
| [Boundaries](docs/17-boundaries.md) | How boundaries partition entities, DbContexts, and transaction scopes |

### Entity model

| Guide | What you'll learn |
|-------|-------------------|
| [Entity System](docs/02-entity-system.md) | `[Entity]`, PersistenceId, `Create()`, setters, `[LogicKey]`, `[PartOf<TParent>]`, identifiers (Guid7, OpaqueId, ShortGuid) |
| [Attributes](docs/03-attributes.md) | `[Auditable]`, `[Audited]`, `[ValueObject]`, `[SoftDelete]`, `[ConcurrencyAware]`, `[Lookup]`, `[StateMachine]` |
| [Relationships](docs/04-relationships.md) | `[Relation.*]` attributes, FK generation, cross-boundary rules |
| [State Machine](docs/19-state-machine.md) | `[StateMachine<T>]` + `[TransitionFrom]` — compile-time guarded status transitions |
| [Advanced Features](docs/08-advanced.md) | Temporal relations, inheritance, hierarchy, polymorphic, lifecycle, presets, batch |

### Data access

| Guide | What you'll learn |
|-------|-------------------|
| [Repository](docs/05-repository.md) | `IRepository<T>`, concrete repos, specifications, `IUnitOfWork`, bulk operations |
| [Mutations](docs/06-mutations.md) | `Mutation<T>`, `ApplyTo()`, collection strategies, modes |
| [Patch](docs/13-patch.md) | `[Patch<T>]` and its property-set tracking — and why tri-state PATCH needs `[GeneratePatch<T>]` instead |

### Querying

| Guide | What you'll learn |
|-------|-------------------|
| [Query System](docs/09-query-system.md) | `[Query<T,R>]`, `[Filter]`, `[Sort]`, `[FilterDto<T>]`, `[Join<T>]`, query executor |
| [Query Filters](docs/07-query-filters.md) | Soft-delete, tenant, permission, visibility filters, toggle, modes |
| [Grid Filtering](docs/10-grid-filtering.md) | `[GridFilter<T>]`, `[GridAdapter<T>]`, DevExpress/PrimeNG adapters |
| [Projections & Views](docs/11-projections-views.md) | `[Projectable]`, `[ComputedFilter]`, `[QueryView<T>]`, aggregations |
| [Query Strategy & Loading](docs/12-datasource-loading.md) | `[QueryStrategy]`, strategies, `[LoadWith<T>]`, split queries |
| [Eager Loading](docs/20-eager-loading.md) | The navigations derived from a DTO's own shape, and what happens when one is missing |
| [Data Ownership & Visibility](docs/18-data-ownership.md) | `[HasOwner]`, `[HasAccessScopes]`, data-level authorization |

### Pipelines & diagnostics

| Guide | What you'll learn |
|-------|-------------------|
| [Query Pipeline](docs/15-query-pipeline.md) | Endpoint → filters → Apply → count/page/project → result |
| [Mutation Pipeline](docs/16-mutation-pipeline.md) | Endpoint → validation → load/apply → persist → events |
| [Diagnostics](docs/14-diagnostics.md) | PRAG06xx/07xx diagnostics, root causes, mitigations |
| [Common Mistakes](docs/common-mistakes.md) | Wrong code → right code for the most common pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Checklists for generation, DI, filter, query, and migration issues |

### EF Core implementation

[DbContext generation](docs/efcore/01-dbcontext-generation.md) ·
[Repository internals](docs/efcore/02-repository-implementation.md) ·
[Entity configuration](docs/efcore/03-entity-configuration.md) ·
[Interceptors & runtime](docs/efcore/04-interceptors-runtime.md) ·
[Bulk operations](docs/efcore/05-bulk-operations.md) ·
[Filter pipeline](docs/efcore/06-filter-pipeline.md) ·
[Testing](docs/efcore/07-testing-generated-persistence.md) ·
[Migration patterns](docs/efcore/08-migration-patterns.md)

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Persistence is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
