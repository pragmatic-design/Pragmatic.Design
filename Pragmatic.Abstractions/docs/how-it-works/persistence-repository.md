# Persistence, repositories — reading and committing

> **Scope**: the repository contracts, which live in two packages.
> `IUnitOfWork` · `ITransaction` · `TransactionState` in
> `src/Pragmatic.Abstractions/Persistence/Repository/`, and
> `IReadRepository<TEntity>` · `IRepository<TEntity>` in
> `Pragmatic.Persistence/src/Pragmatic.Persistence/Repository/`.
>
> ⚠️ Both halves share the namespace `Pragmatic.Persistence.Repository`, not the assembly, so one
> `using` covers them. The repositories cannot live in Abstractions because `RunAsync` — running a
> declared `[Query]` through the repository — answers with `PagedResult<T>`, which reaches
> `Pragmatic.Result` through `QueryError`, and `Pragmatic.Result` references
> `Pragmatic.Abstractions`. An interface declared there could not name the type its own signature
> returns.
>
> **Not covered here**: the entity contracts these repositories move around are
> [their own document](persistence-entity.md). `ISpecification<T>`, used by four of the read
> methods, is declared in `Specification/` — the `Specification<T>` base class you derive from lives
> in the `Pragmatic.Specification` package.

For the member-by-member catalogue, see
[interfaces](../interfaces.md#3-persistence----repositories). This document is about how the pieces
fit together.

## Three layers, deliberately not one

`IReadRepository` is queries. `IRepository` adds writes. `IUnitOfWork` commits. They are separate so
a signature says what a collaborator is allowed to do: a pricing service that takes
`IReadRepository<Room>` has no write method to call. That is a strong signal, not an
airtight one — `Query()` returns `IQueryable<TEntity>`, and EF Core's `ExecuteDeleteAsync` and
`ExecuteUpdateAsync` are extension methods on it. Writing through a read repository costs the caller
a `using Microsoft.EntityFrameworkCore`, which is the same tell the `Query()` section below calls the
marker of having left the domain layer. The invariant is convention made visible by the signatures,
not something the type system enforces.

You do not implement any of them. The source generator emits one concrete repository per entity — a
`Repository` class nested in the entity's partial, so `Property.Repository` — base list
`IRepository<TEntity>`, with the specification methods, `Query()`, and the filter plumbing
already wired, and registers it three times:

```csharp
services.AddScoped<Property.Repository>();
services.AddScoped<IRepository<Property>>(sp => sp.GetRequiredService<Property.Repository>());
services.AddScoped<IReadRepository<Property>>(sp => sp.GetRequiredService<Property.Repository>());
```

One instance, three doors. Alongside these three the same method also registers the boundary's `DbContext` keyed by
the boundary marker type — the same keying the next section describes for `IUnitOfWork`.
`Pragmatic.Persistence.EFCore` contributes the `DbContext`, the interceptors and `EfCoreUnitOfWork` —
it does not contain the repositories themselves.

## `IUnitOfWork` is a keyed service

This is the fact that changes how you write code, and it is not visible from the interface.

`IUnitOfWork` is registered **only** as a keyed scoped service, keyed by the boundary marker type:

```csharp
services.AddKeyedScoped<IUnitOfWork>(typeof(CatalogBoundary), (sp, _) =>
    new EfCoreUnitOfWork(sp.GetRequiredService<CatalogDbContext>()));
```

There is no non-keyed registration anywhere. So:

- generated invokers receive it as
  `[FromKeyedServices(typeof(CatalogBoundary))] IUnitOfWork unitOfWork`;
- hand-written code asks for `sp.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary))` —
  which is what the Showcase endpoints that batch several entities do;
- **a plain `IUnitOfWork uow` constructor parameter does not resolve.**

The key is the boundary because the unit of work *is* the boundary's `DbContext`. A host with two
boundaries has two of them, and an unkeyed registration could only ever be wrong for one of them.

For a single logical operation on one aggregate you do not need it at all: the generated repository
exposes `SaveChangesAsync`. That method is on the nested generated type, not on `IRepository`, so the
shortcut requires depending on `Property.Repository`; a caller holding the interface — as
`PatchAmenityEndpoint` does — resolves the keyed unit of work even for a one-entity save. Reach for
the unit of work when the operation spans repositories, and
for `ExecuteInTransactionAsync` (in `Pragmatic.Persistence`, `UnitOfWorkExtensions`) when it should
succeed or fail as a whole — it wraps the work in a transaction and returns a `Result`, which is why
it lives one package up: `Pragmatic.Abstractions` does not depend on `Pragmatic.Result`.

## Default interface members, and what they are for

Only `SaveChangesAsync` and `BeginTransactionAsync` are abstract; everything else on `IUnitOfWork`
ships with a default body. They exist so a stub or in-memory unit of work stays source-compatible
when the interface grows, and each default was chosen to be loud rather than convenient.

| Member | Default | Why |
|---|---|---|
| `Add(object entity)` | throws `NotSupportedException` | Preset and child-entity insertion depends on this working. A silent no-op would drop rows and report success. |
| `State` | `TransactionState.None` | An implementation that owns no transaction has no state to report; one that does overrides. |
| `SavepointAsync` / `RollbackToSavepointAsync` | throw `NotSupportedException` | Not every provider has savepoints. Failing loudly beats pretending to roll back. |
| `IsCommitted` | `State == TransactionState.Committed` | Convenience over the enum; it follows whatever `State` reports, so overriding `State` is enough. |

`EfCoreUnitOfWork` overrides the rest: `Add` forwards to `DbContext.Add`, `State` goes `Active` on
begin and flips through the commit and rollback callbacks, and the savepoint pair delegates to the
ambient EF Core transaction.

## Specifications go through the filters

All four specification methods — `FindAsync`, `CountAsync`, `ExistsAsync`, `FirstOrDefaultAsync` —
take `ISpecification<TEntity>`, and the generated repository composes them the same way:

```csharp
return ApplyFilters(Set).Where(spec.ToExpression())…
```

So soft-delete, tenant and ownership filters apply on the specification path exactly as they do on
the ordinary one. A specification narrows a query; it never widens the set of rows you are allowed
to see.

## `Query()`, and where it belongs

`Query()` returns `IQueryable<TEntity>` and, by construction, leaks the EF Core query provider
through the abstraction — that is the price of composing projections, `Include`s and
provider-specific operators at the call site. The contract's own documentation asks you to keep
those callers in the infrastructure layer.

There is a second reason to know where the boundary is. The generated repository also has a
`Query(QueryStrategy)` overload — `Projection`, `Entity`, `Filtered`, `Raw` — that is **not** on the
interface. `Query()` is `Query(Filtered)`; code that needs any other strategy depends on the concrete
generated repository type, and that dependency is the marker that you have left the domain layer.
`QueryStrategy` is not the filter-mode enum: `Admin` belongs to `FilterMode`, a different one.

## `ITransaction` and `TransactionState`

`ITransaction` is the handle `IUnitOfWork.BeginTransactionAsync` returns: `CommitAsync`,
`RollbackAsync`, a `TransactionId`, and both dispose patterns. `TransactionState` is the enum
`State` reports.

The implementations live entirely inside `Pragmatic.Persistence.EFCore` — `EfCoreTransaction`
implements one, `EfCoreUnitOfWork` produces it and maintains the other. Consumption does not:
`UnitOfWorkExtensions.ExecuteInTransactionAsync`, one package up in `Pragmatic.Persistence`, begins
the transaction and commits or rolls it back in each of its three overloads. A non-EF `IUnitOfWork`
therefore has to return an `ITransaction` that works there too. The source generator never names
either. They are the seam for a non-EF unit of work, not something application code is expected to
touch.

## External references

Named here, described where they live:

- **`Pragmatic.Persistence.EFCore`** — `EfCoreUnitOfWork` and `EfCoreTransaction`, the
  implementations behind every contract on this page, plus the `DbContext` and interceptors.
- **`Pragmatic.Persistence`** — `UnitOfWorkExtensions.ExecuteInTransactionAsync<T, TError>`, the
  `Result`-returning transactional helper; and `Query/` where `QueryStrategy` and the filter modes
  are defined.
- **`Pragmatic.Actions`** — the generated invokers take the keyed `IUnitOfWork` and override
  `SaveChangesAsync`; `MutationInvoker` uses `Query()` (with `IgnoreQueryFilters` where the
  lifecycle demands it) to load the entity it is about to change.
- **`Pragmatic.Specification`** — the `Specification<T>` base class and its combinators; the
  interface the repository speaks is `ISpecification<T>` in `Pragmatic.Abstractions`.
- **`Pragmatic.Endpoints`** — endpoints are the layer where `IReadRepository` and `Query()` are
  meant to meet, and the Showcase endpoints are the worked examples.
