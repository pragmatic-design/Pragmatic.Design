---
title: "Getting Started"
description: "`Pragmatic.Persistence` is the core contract layer for the Pragmatic persistence stack."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/01-getting-started.md
sidebar:
  order: 2
---
## What Is Pragmatic.Persistence?

`Pragmatic.Persistence` is the core contract layer for the Pragmatic persistence stack.

It gives you:

- entity and relationship attributes
- repository and unit-of-work interfaces
- query/filter primitives
- lifecycle hooks and supporting types

Generated persistence code is emitted when the consuming project also references:

- `Pragmatic.Persistence.EFCore`
- the `Pragmatic.SourceGenerator` analyzer

Without those two pieces, you still have the abstractions, but not the generated repositories, DbContexts, or entity members.

## Activation Requirements

For a project that wants generated persistence code, the minimum setup is:

```bash
dotnet add package Pragmatic.Persistence
dotnet add package Pragmatic.Persistence.EFCore
```

And the analyzer that does the generating:

```bash
dotnet add package Pragmatic.SourceGenerator
```

Inside this repository the same reference is a project, marked as an analyzer so its assembly is not
linked into yours:

```xml
<ProjectReference Include="..\Pragmatic.SourceGenerator\src\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## The Mental Model

Think about the stack in three layers:

1. Your code
   Entities, DTOs, boundaries, DbContexts, and attributes that describe intent.
2. Analyzer and source generator
   Reads those declarations at compile time and emits `.g.cs` files.
3. Generated code
   Entity members, repositories, filters, configuration, and DI registration.

You never edit generated files directly. You change the source declarations and rebuild.

## Partial Classes

Every generated persistence type must be declared as `partial`.

```csharp
[Entity]
public partial class Order
{
    public string Name { get; private set; } = "";
}
```

Forget it and **PRAG0600** stops generation before the code can become inconsistent — with a code fix
attached, so the IDE offers to add the keyword. One diagnostic per feature says the same thing:
PRAG0600 for `[Entity]`, PRAG0406 for `[Boundary]`, PRAG0400 for actions and mutations, PRAG0500 for
endpoints.

## What Gets Generated

Given an entity like this:

```csharp
[Entity]
[Auditable]
[SoftDelete]
[BelongsTo<SalesBoundary>]
public partial class Order
{
    [LogicKey]
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

The generator emits:

Every per-type file is prefixed with the entity's namespace, so two entities sharing a simple name
across namespaces cannot collide:

| Generated artifact | What it contains |
|-------------------|------------------|
| `Sales.Order.Traits.g.cs` | `PersistenceId`, `Id`, **and** the `[Auditable]` / `[SoftDelete]` properties — one file, not one per trait |
| `Sales.Order.Create.g.cs` | Static `Create(...)` factory |
| `Sales.Order.Setters.g.cs` | `internal Set{Property}(...)` with change tracking, plus `ModifiedProperties` and `ResetModifiedProperties()` |
| `Sales.Order.Specs.g.cs` | `OrderSpecifications.ById(...)`, and `ByOrderNumber(...)` from the `[LogicKey]` |
| `Sales.Order.Repository.g.cs` | Nested `Order.Repository` implementing `IRepository<Order>`, plus logic-key lookups and bulk methods |
| `Sales.Order.SoftDeleteFilter.g.cs` | Nested `Order.SoftDeleteFilter` query filter |
| `EntityConfig.Sales.Order.g.cs` | EF Core `IEntityTypeConfiguration<Order>` (host-level) |
| `_Infra.Persistence.*.g.cs` | DI registration: `RepositoryRegistration`, `DbContextRegistration`, `QueryFilters`, `FilterMap` |

The rows above assume the project references both EF Core support and the analyzer.

## Boundaries

A boundary is the logical group that binds entities, repositories, and unit-of-work scope together.
For persistence it is a plain marker type: `[BelongsTo<T>]` on the entity is what assigns it, and the
names of the generated types come from the class with the `Boundary` suffix trimmed
(`SalesBoundary` → `SalesDbContext`). Add `[Boundary]` — the `Pragmatic.Actions` one, which requires
`partial` — when you also want the namespace's operations grouped into an `ISalesActions` interface.

```csharp
public sealed class SalesBoundary;

[Entity]
[BelongsTo<SalesBoundary>]
public partial class Order
{
    public decimal Total { get; private set; }
}
```

Boundaries matter because:

- `DbContext` generation is boundary-based
- repositories resolve the correct `DbContext` through keyed DI
- `IUnitOfWork` is registered keyed by boundary

## PersistenceId

Every generated entity gets a `PersistenceId` property, and it is a `Guid`. The generated trait
assigns it at construction — `PersistenceId { get; set; } = Guid.CreateVersion7()` — and the EF
configuration declares it `ValueGeneratedNever()`, so the store does not claim the same job.

`Id` is a read-only alias of `PersistenceId` (`public Guid Id => PersistenceId;`). Declare
`PersistenceId` yourself and the trait steps aside, EF configuration included.

## Minimal Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;

public sealed class StoreBoundary;

[Entity]
[BelongsTo<StoreBoundary>]
public partial class Product
{
    [LogicKey]
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public decimal Price { get; private set; }
}

public sealed class ProductService(
    Product.Repository products,          // the concrete type: FindBySku needs it
    IServiceProvider services)
{
    public async Task<Guid> CreateProduct(string sku, string name, decimal price, CancellationToken ct)
    {
        var product = Product.Create(sku, name, price);
        products.Add(product);

        var uow = services.GetRequiredKeyedService<IUnitOfWork>(typeof(StoreBoundary));
        await uow.SaveChangesAsync(ct);

        return product.PersistenceId;
    }

    public Task<Product?> FindBySku(string sku, CancellationToken ct)
        => products.GetBySkuAsync(sku, ct);
}
```

Why the concrete repository in the example: `GetBySkuAsync(...)` is generated on
`Product.Repository`, not on `IRepository<Product>` — the stable interface stays intentionally
smaller. Injecting the interface and calling `GetBySkuAsync` on it does not compile.

Use `IRepository<T>` / `IReadRepository<T>` when you want your application code to depend only on the stable contract surface.

## Next Steps

- [Entity System](/modules/persistence/02-entity-system/) — Define entities, relationships, and attributes
- [Repository](/modules/persistence/05-repository/) — CRUD operations and specifications
- [Query Pipeline](/modules/persistence/15-query-pipeline/) — How queries flow from HTTP to SQL
- [Mutation Pipeline](/modules/persistence/16-mutation-pipeline/) — How mutations flow through validation and persistence
- [Boundaries](/modules/persistence/17-boundaries/) — Logical partitions and transaction scopes
- [Query Filters](/modules/persistence/07-query-filters/) — Automatic soft-delete, tenant, and visibility filtering
- [Data Sources & Loading](/modules/persistence/12-datasource-loading/) — Control tracking, includes, and filter behavior
- [Diagnostics Guide](/modules/persistence/14-diagnostics/) — Understanding PRAG06xx/07xx diagnostics
