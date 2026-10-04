---
title: "Repository"
description: "Every entity needs the same core data-access operations:"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/05-repository.md
sidebar:
  order: 6
---
## The Problem

Every entity needs the same core data-access operations:

- load by primary key
- query by specification
- add, update, remove
- save through a unit of work

On top of that, every generated repository adds logic-key lookups, include overloads and bulk operations.

The important detail is that the stable interface surface is intentionally smaller than the generated concrete repository surface.

## Two API Layers

There are two repository surfaces in the persistence stack.

### 1. Stable abstractions

These are the interfaces you should prefer for application code that only needs the common CRUD/specification contract:

```csharp
public interface IRepository<TEntity> : IReadRepository<TEntity>

{
    void Add(TEntity entity);
    void AddRange(IEnumerable<TEntity> entities);
    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
    void Update(TEntity entity);
}

public interface IReadRepository<TEntity>

{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TEntity>> FindAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<bool> ExistsAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> spec, CancellationToken ct = default);

    // Runs a declared [Query] against this repository's own set.
    Task<IReadOnlyList<TResult>> RunAsync<TResult>(IQuery<TEntity, TResult> query, CancellationToken ct = default);
    Task<PagedResult<TResult>> RunAsync<TResult>(IPagedQuery<TEntity, TResult> query, CancellationToken ct = default);

    IQueryable<TEntity> Query();
}
```

`RunAsync` is the reuse of a read that is already declared: the same `[Query]` its own HTTP route
answers with, filters, sort and `[WithoutFilter<T>]` included, run from inside an operation that
already holds the repository. The `DbContext` never appears in the calling code, which is the point:
a read declared once is a read used everywhere, instead of LINQ written twice for one shape.

The source is the repository's **set**, not `Query()`: `Query()` has already applied the filters, and
handing an already-filtered source to an executor that filters again changes which filters are in
force rather than repeating them. It is invisible on an ordinary query and shows up exactly under
`[FilterMode]` or `[WithoutFilter<T>]`.

⚠️ **No operation pipeline runs on this path**, deliberately: the caller is inside a `[DomainAction]`
that already has its own validation, permission and transaction. The path *with* the pipeline is the
query's own invoker, which an operation reaches with `[LoadFrom<TQuery>]` on a property of the query's
answer: its inputs bound by name from the operation, its permission asked of the caller, its failure the
operation's. Choose `RunAsync` to reuse a read inside the operation's own authorization; choose
`[LoadFrom]` to run the query as the query.

⚠️ The boundary's **internal** facade (`I{Boundary}InternalActions`) also runs the query's invoker, but
inside an internal call: the query's permission is not asked. That is right between operations the boundary
trusts, and wrong as the way an operation reads data on its caller's behalf.

⚠️ It answers `IReadOnlyList<TResult>`, and C# forbids a user-defined conversion whose source is an
interface, so it does not convert implicitly to `Result<IReadOnlyList<T>, IError>` the way a `List<T>`
does. `Result<…>.Success(rows)` is the form.

### 2. Generated concrete repository

The analyzer generates a concrete repository class per entity. Beyond the interface it always adds:

| Member | Condition |
|---|---|
| `Set` (the `DbSet<T>`), `Context` (`internal`) | always |
| `GetByIdAsync(id, includes, ct)` | always |
| `Query(QueryStrategy strategy)`: `Projection`, `Entity`, `Filtered`, `Raw` | always |
| `BulkInsertAsync`, `BulkUpsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync`, `UpsertAsync` | always |
| `SaveChangesAsync(ct)` | always, but the return type is `Result<int, ConcurrencyError>` on a `[ConcurrencyAware]` entity and `Task<int>` on any other |
| `GetBy{LogicKey}Async(...)`, or `GetBy{A}And{B}Async(...)` for a composite one | only with `[LogicKey]` |

Those methods are available on the generated concrete repository type, not on `IRepository<TEntity>`.

## What the generated repository looks like

The repository is generated as a **nested class** inside the entity's partial class. This keeps the repository co-located with its entity and avoids top-level class proliferation.

```csharp
// Generated in {Namespace}.Order.Repository.g.cs
public partial class Order
{
  public partial class Repository : IRepository<Order>
  {
    // The constructor is generated too, and this is what the container fills:
    public Repository(
        DbContext db, IUnitOfWork unitOfWork,
        IQueryFilterProvider? filterProvider = null, FilterMapComposer? filterMapComposer = null,
        ITenantContext? tenantContext = null, IQueryFilterToggle? filterToggle = null,
        IEnumerable<RollUpRule>? rollUpRules = null);

    public DbSet<Order> Set { get; }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    public Task<List<Order>> FindAsync(ISpecification<Order> spec, CancellationToken ct = default);
    public Task<int> CountAsync(ISpecification<Order> spec, CancellationToken ct = default);
    public Task<bool> ExistsAsync(ISpecification<Order> spec, CancellationToken ct = default);
    public Task<Order?> FirstOrDefaultAsync(ISpecification<Order> spec, CancellationToken ct = default);
    public IQueryable<Order> Query();
    public IQueryable<Order> Query(QueryStrategy strategy);

    public void Add(Order entity);
    public void AddRange(IEnumerable<Order> entities);
    public void Remove(Order entity);
    public void RemoveRange(IEnumerable<Order> entities);
    public void Update(Order entity);

    // Concrete-repository-only helpers
    public Task<Order?> GetByOrderNumberAsync(string key, CancellationToken ct = default);
    public Task<Order?> GetByIdAsync(
        Guid id,
        Func<IQueryable<Order>, IQueryable<Order>> includes,
        CancellationToken ct = default);

    // Bulk, on every entity
    public Task<int> BulkInsertAsync(IReadOnlyList<Order> entities, BulkInsertOptions? options = null, CancellationToken ct = default);
    public Task<int> BulkUpsertAsync(IReadOnlyList<Order> entities, UpsertOptions? options = null, CancellationToken ct = default);
    public Task<int> BulkUpdateAsync(ISpecification<Order> filter, Action<UpdateSettersBuilder<Order>> update, CancellationToken ct = default);
    public Task<int> BulkDeleteAsync(ISpecification<Order> filter, CancellationToken ct = default);
    public Task<int> UpsertAsync(Order entity, UpsertMatch matchOn = UpsertMatch.PrimaryKey, CancellationToken ct = default);

    public Task<int> SaveChangesAsync(CancellationToken ct = default);
  }
}
```

## Which one should you inject?

| Need | Inject |
|------|--------|
| Stable CRUD/specification access | `IRepository<TEntity>` |
| Read-only stable access | `IReadRepository<TEntity>` |
| Logic-key helper or include overload | `Order.Repository` (nested concrete type) |
| Bulk methods | `Order.Repository` (nested concrete type) |

## Using the stable interface

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class OrderService(
    IRepository<Order> orders,
    IServiceProvider services)
{
    public async Task<Guid> PlaceOrder(string number, decimal total, CancellationToken ct)
    {
        var order = Order.Create(number, total);
        orders.Add(order);

        var uow = services.GetRequiredKeyedService<IUnitOfWork>(typeof(SalesBoundary));
        await uow.SaveChangesAsync(ct);

        return order.PersistenceId;
    }

    public Task<List<Order>> GetLargeOrders(CancellationToken ct)
        => orders.FindAsync(Spec<Order>.Where(o => o.Total > 1000), ct);
}
```

## Using the concrete repository

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

public sealed class OrderQueries(
    Order.Repository orders,
    IServiceProvider services)
{
    public Task<Order?> GetByNumber(string orderNumber, CancellationToken ct)
        => orders.GetByOrderNumberAsync(orderNumber, ct);

    public Task<Order?> GetWithItems(Guid id, CancellationToken ct)
        => orders.GetByIdAsync(id, q => q.Include(o => o.LineItems), ct);

    public Task<int> SaveAsync(CancellationToken ct)
    {
        var uow = services.GetRequiredKeyedService<IUnitOfWork>(typeof(SalesBoundary));
        return uow.SaveChangesAsync(ct);
    }
}
```

## Automatic filtering

Every generated read path applies root filters automatically through `IQueryFilterProvider`.

That means:

- soft-deleted rows are excluded
- tenant and permission filters can be enforced centrally
- temporal filters are applied consistently

Use `IQueryFilterToggle` when a test, migration, or admin path needs to change filter behavior.

## Unit of Work

`IUnitOfWork` is the persistence-agnostic save/transaction contract:

```csharp
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    void Add(object entity);
    Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default);

    // Stops tracking one entity, so a failed save does not leave it for the next one to retry
    void Detach(object entity);

    // Where the ambient transaction is, without catching exceptions to find out
    TransactionState State { get; }
    bool IsCommitted { get; }

    // Undo part of a transaction without aborting all of it
    Task SavepointAsync(string name, CancellationToken ct = default);
    Task RollbackToSavepointAsync(string name, CancellationToken ct = default);
}
```

⚠️ **`Detach` matters more than it looks.** A change tracker keeps what it was given whether the save
succeeded or not, so after a failure the entity is still pending and the *next* save tries to write it
again: an import committing row by row cannot report one bad row and carry on. The mutation pipeline
calls it on every save path; reach for it yourself when you save through the unit of work directly.

Transactions are represented by `ITransaction`, not plain `IAsyncDisposable`.

```csharp
await using var tx = await uow.BeginTransactionAsync(ct);
try
{
    orders.Add(order);
    await uow.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
}
catch
{
    await tx.RollbackAsync(ct);
    throw;
}
```

## DI registration

**Inside a Pragmatic host you call none of this.** The host generator emits the calls itself, and its
`Host.Services.g.cs` is where they are: DbContexts, repositories, query filters, domain actions,
endpoints and startup steps are all registered before your `IStartupStep` runs.

Two are the exception, and stay yours to call from an `IStartupStep`: nothing calls them for you, and
the feature silently does nothing if you forget:

```csharp
services.AddShowcaseLookupCaches();      // [Lookup]   → _Infra.Persistence.LookupCache.g.cs
services.AddShowcaseCascadeHandlers();   // [CascadeOn] → _Infra.Persistence.CascadeHandlers.g.cs
```

Outside a Pragmatic host (a test project, a console app) the same extensions are what you wire by
hand:

| Extension | Emitted in | Note |
|---|---|---|
| `Add{Boundary}DbContext(o => …)` | `_Infra.Persistence.DbContextRegistration.g.cs` | one per boundary; `AddAllPragmaticDbContexts(o => …)` does the lot with one option callback |
| `AddPragmaticPersistenceRepositories<TDbContext>()` | `_Infra.Persistence.RepositoryRegistration.g.cs` | the type argument picks the DbContext the repositories resolve |
| `Add{Prefix}QueryFilters()` | `_Infra.Persistence.QueryFilters.g.cs` | `{Prefix}` is the identifier derived from the entities' common namespace |

Important:

- repositories are registered `Scoped` and unkeyed: the concrete `Order.Repository`, and
  `IRepository<Order>` / `IReadRepository<Order>` resolving to the same instance
- `IUnitOfWork` is registered keyed by boundary
- logic-key helpers live on the generated repository class, not on the interface

## Specifications

A specification is a reusable predicate:

```csharp
var largeOrders = Spec<Order>.Where(o => o.Total > 1000);
var pendingLargeOrders = largeOrders.And(Spec<Order>.Where(o => o.Status == OrderStatus.Pending));

var count = await orders.CountAsync(largeOrders, ct);
var list = await orders.FindAsync(pendingLargeOrders, ct);
var first = await orders.FirstOrDefaultAsync(pendingLargeOrders, ct);
```

## Related guides

- [Getting Started](/modules/persistence/01-getting-started/)
- [Query Filters](/modules/persistence/07-query-filters/)
- [Testing Generated Persistence](/modules/persistence/efcore-07-testing-generated-persistence/)
