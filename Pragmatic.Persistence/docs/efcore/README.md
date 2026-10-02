# Pragmatic.Persistence.EFCore

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
| Every entity needs manual `IEntityTypeConfiguration<T>` | Generated config from `[Entity]`, `[Relation.*]`, `[SoftDelete]`, `[ConcurrencyAware]` | [Entity Configuration](03-entity-configuration.md) |
| DbContext setup is boilerplate (`DbSet`, `OnModelCreating`, DI) | `[PragmaticDatabase]` + `[Include<TModule, TDatabase>]` → complete DbContext generation | [DbContext Generation](01-dbcontext-generation.md) |
| Repository CRUD is the same shape for every entity | Generated repos with CRUD, specs, logic-key helpers, include overloads, bulk methods | [Repository Implementation](02-repository-implementation.md) |
| Timestamps, tenant and ownership fields are easy to forget | `AuditingInterceptor`, `TenantInterceptor`, `OwnershipInterceptor`, `SoftDeleteInterceptor` run on `SaveChanges` | [Interceptors & Runtime](04-interceptors-runtime.md) |
| `Include()` bypasses soft-delete and tenant filters | `PragmaticQueryFilterVisitor` injects `.Where()` into navigation expressions | [Filter Pipeline](06-filter-pipeline.md) |
| Inserting thousands of records one by one is slow | Generated `BulkInsertAsync`, `BulkUpsertAsync` with provider-specific SQL | [Bulk Operations](05-bulk-operations.md) |
| Adding `[SoftDelete]` to existing entities needs safe migration | Documented rollout patterns with data backfill and rollback plans | [Migration Patterns](08-migration-patterns.md) |
| Integration tests need a real database with filter setup | Test harness with Testcontainers, fake `ICurrentUser`, filter toggle | [Testing](07-testing-generated-persistence.md) |

---

## Documentation

| Guide | What You'll Learn |
|-------|-------------------|
| [DbContext Generation](01-dbcontext-generation.md) | `[PragmaticDatabase]`, `[Include<,>]`, DI registration, multi-provider support |
| [Repository Implementation](02-repository-implementation.md) | Generated repository shape, keyed DI, soft-delete `Remove()`, `IUnitOfWork` |
| [Entity Configuration](03-entity-configuration.md) | Property mapping, indexes, relationships, inheritance, two-level query filters |
| [Interceptors & Runtime](04-interceptors-runtime.md) | where the ID comes from, auditing, tenant, soft-delete and ownership interceptors, value converters |
| [Bulk Operations](05-bulk-operations.md) | Bulk insert/update/delete/upsert, provider SQL, concurrency options |
| [Filter Pipeline](06-filter-pipeline.md) | FilterMapComposer, expression visitor, EfCoreQueryExecutor integration |
| [Testing](07-testing-generated-persistence.md) | Testcontainers PostgreSQL, fixture setup, fake current user, filter toggles |
| [Migration Patterns](08-migration-patterns.md) | Safe rollout for `[SoftDelete]`, `[Auditable]`, and schema-changing attributes |

For entity-level concepts (attributes, relationships, queries, mutations), see the [Pragmatic.Persistence documentation](../../README.md).

## Requirements

- .NET 10.0+
- Microsoft.EntityFrameworkCore 10.0+

## License

Part of the [Pragmatic.Design](../../../README.md) ecosystem.
