---
title: "Pragmatic.Persistence.EFCore"
description: "EF Core runtime implementation for the Pragmatic persistence stack."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/efcore/README.md
sidebar:
  order: 30
---
EF Core runtime implementation for the Pragmatic persistence stack.

## Overview

`Pragmatic.Persistence.EFCore` bridges the gap between Pragmatic entity declarations and a working EF Core data layer. Where `Pragmatic.Persistence` provides the attributes and interfaces, this package provides the runtime: generated DbContexts, repository implementations, interceptors, the filter pipeline, and bulk operations.

You declare a **database**, the host says which module lives on it, and you get a fully configured
data layer:

```csharp
// In the host: declare the database…
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Sales")]
public sealed class SalesDatabase : PragmaticDatabase;

// …and say which module lives on it. That pairing IS the topology.
[Include<SalesModule, SalesDatabase>]
public sealed class AppHostModule;
```

You never write a `DbContext`: the generator emits one per boundary from that pairing, and the
generated host registers each of them for you.

The source generator reads each boundary's entities and produces `OnModelCreating` with all
`IEntityTypeConfiguration<T>` calls, `DbSet` properties, interceptor registration, and an
`Add{Boundary}DbContext()` extension method for DI.

Inside a Pragmatic host that is all of it — the generated host registers the DbContexts, the
repositories and the query filters before your `IStartupStep` runs. Wiring it by hand, outside a host,
the three calls are:

```csharp
builder.Services.AddSalesDbContext(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Sales")));
builder.Services.AddPragmaticPersistenceRepositories<SalesDbContext>();
builder.Services.AddMyAppQueryFilters();
```

This package is designed to work alongside `Pragmatic.Persistence` and the `Pragmatic.SourceGenerator` analyzer. All three are required for the full generated output.

## Installation

```bash
dotnet add package Pragmatic.Persistence
dotnet add package Pragmatic.Persistence.EFCore
```

Add the analyzer as a project reference:

```xml
<ProjectReference Include="..\Pragmatic.SourceGenerator\src\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

---

## Feature Catalog

Each feature below addresses a specific problem in EF Core data-access code. The links lead to detailed guides with examples, generated output, and design rationale.

| Problem | Solution | Guide |
|---------|----------|-------|
| Every entity needs manual `IEntityTypeConfiguration<T>` | Generated config from `[Entity]`, `[Relation.*]`, `[SoftDelete]`, `[ConcurrencyAware]` | [Entity Configuration](/modules/persistence/efcore-03-entity-configuration/) |
| DbContext setup is boilerplate (`DbSet`, `OnModelCreating`, DI) | `[PragmaticDatabase]` + `[Include<TModule, TDatabase>]` → complete DbContext generation | [DbContext Generation](/modules/persistence/efcore-01-dbcontext-generation/) |
| Repository CRUD is the same shape for every entity | Generated repos with CRUD, specs, logic-key helpers, include overloads, bulk methods | [Repository Implementation](/modules/persistence/efcore-02-repository-implementation/) |
| Timestamps, tenant and ownership fields are easy to forget | `AuditingInterceptor`, `TenantInterceptor`, `OwnershipInterceptor`, `SoftDeleteInterceptor` run on `SaveChanges` | [Interceptors & Runtime](/modules/persistence/efcore-04-interceptors-runtime/) |
| `Include()` bypasses soft-delete and tenant filters | `PragmaticQueryFilterVisitor` injects `.Where()` into navigation expressions | [Filter Pipeline](/modules/persistence/efcore-06-filter-pipeline/) |
| Inserting thousands of records one by one is slow | Generated `BulkInsertAsync`, `BulkUpsertAsync` with provider-specific SQL | [Bulk Operations](/modules/persistence/efcore-05-bulk-operations/) |
| Adding `[SoftDelete]` to existing entities needs safe migration | Documented rollout patterns with data backfill and rollback plans | [Migration Patterns](/modules/persistence/efcore-08-migration-patterns/) |
| Integration tests need a real database with filter setup | Test harness with Testcontainers, fake `ICurrentUser`, filter toggle | [Testing](/modules/persistence/efcore-07-testing-generated-persistence/) |

---

## Documentation

| Guide | What You'll Learn |
|-------|-------------------|
| [DbContext Generation](/modules/persistence/efcore-01-dbcontext-generation/) | `[PragmaticDatabase]`, `[Include<,>]`, DI registration, multi-provider support |
| [Repository Implementation](/modules/persistence/efcore-02-repository-implementation/) | Generated repository shape, keyed DI, soft-delete `Remove()`, `IUnitOfWork` |
| [Entity Configuration](/modules/persistence/efcore-03-entity-configuration/) | Property mapping, indexes, relationships, inheritance, two-level query filters |
| [Interceptors & Runtime](/modules/persistence/efcore-04-interceptors-runtime/) | where the ID comes from, auditing, tenant, soft-delete and ownership interceptors, value converters |
| [Bulk Operations](/modules/persistence/efcore-05-bulk-operations/) | Bulk insert/update/delete/upsert, provider SQL, concurrency options |
| [Filter Pipeline](/modules/persistence/efcore-06-filter-pipeline/) | FilterMapComposer, expression visitor, EfCoreQueryExecutor integration |
| [Testing](/modules/persistence/efcore-07-testing-generated-persistence/) | Testcontainers PostgreSQL, fixture setup, fake current user, filter toggles |
| [Migration Patterns](/modules/persistence/efcore-08-migration-patterns/) | Safe rollout for `[SoftDelete]`, `[Auditable]`, and schema-changing attributes |

For entity-level concepts (attributes, relationships, queries, mutations), see the [Pragmatic.Persistence documentation](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Persistence/README.md).

## Requirements

- .NET 10.0+
- Microsoft.EntityFrameworkCore 10.0+

## License

Part of the [Pragmatic.Design](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/README.md) ecosystem.
