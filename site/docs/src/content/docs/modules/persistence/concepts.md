---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Persistence exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this bef"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Persistence exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this before diving into the individual feature guides.

---

## The Problem

EF Core is a capable ORM. But every project that uses it accumulates the same boilerplate: entity identity, audit trails, soft-delete fields, repository patterns, FK configuration, query filters, migration contexts, DI wiring. The effort is not in any single line of code -- it is in the hundreds of lines that repeat identically across every entity in the system.

### A typical entity: manual everything

```csharp
// Entity: you write every property, every interface, every factory
public class Order : IEntity, IAuditable, ISoftDelete
{
    public Guid Id { get; set; }
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }

    // Audit fields: repeated on every auditable entity
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Soft-delete fields: repeated on every soft-deletable entity
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    // Navigation properties: manual FK + nav
    public Guid CustomerId { get; private set; }
    public Customer? Customer { get; set; }
    public ICollection<LineItem> Items { get; } = [];

    // Factory method: hand-written
    public static Order Create(string orderNumber, decimal total, Guid customerId)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Total = total,
            CustomerId = customerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    // Setters: hand-written per property
    public void SetTotal(decimal value) => Total = value;
    public void SetOrderNumber(string value) => OrderNumber = value;

}
```

```csharp
// Repository: every entity gets one
public class OrderRepository : IRepository<Order>
{
    private readonly AppDbContext _db;

    public OrderRepository(AppDbContext db) => _db = db;

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<List<Order>> FindAsync(Expression<Func<Order, bool>> predicate, CancellationToken ct)
        => _db.Orders.Where(predicate).ToListAsync(ct);

    public void Add(Order entity) => _db.Orders.Add(entity);
    public void Remove(Order entity) => _db.Orders.Remove(entity);
    public void Update(Order entity) => _db.Entry(entity).State = EntityState.Modified;

    // Logic key lookup: hand-written
    public Task<Order?> GetByOrderNumberAsync(string number, CancellationToken ct)
        => _db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == number, ct);
}
```

```csharp
// EF Core configuration: per entity
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.OrderNumber).IsUnique();
        builder.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId);
        builder.HasMany(e => e.Items).WithOne().HasForeignKey(e => e.OrderId);
        builder.HasQueryFilter(e => !e.IsDeleted);
        builder.Property(e => e.OrderNumber).HasMaxLength(50);
    }
}
```

```csharp
// Query: manual filter/sort/page
public async Task<PagedResult<OrderDto>> SearchOrders(
    string? customerName, OrderStatus? status,
    SortDirection? dateSort, int page = 1, int pageSize = 20)
{
    var query = _db.Orders.AsNoTracking().Where(o => !o.IsDeleted);

    if (customerName is not null)
        query = query.Where(o => o.Customer!.Name.Contains(customerName));
    if (status is not null)
        query = query.Where(o => o.Status == status);

    query = dateSort == SortDirection.Ascending
        ? query.OrderBy(o => o.CreatedAt)
        : query.OrderByDescending(o => o.CreatedAt);

    var total = await query.CountAsync();
    var items = await query
        .Skip((page - 1) * pageSize).Take(pageSize)
        .Select(o => new OrderDto { /* map 15 properties */ })
        .ToListAsync();

    return new PagedResult<OrderDto>(items, total, page, pageSize);
}
```

```csharp
// DI registration: per entity, per boundary
services.AddScoped<IRepository<Order>, OrderRepository>();
services.AddScoped<IRepository<LineItem>, LineItemRepository>();
services.AddScoped<IRepository<Customer>, CustomerRepository>();
// ... for every entity
```

For a single entity with auditing, soft-delete, a relationship, a logic key, and a search query, that is roughly 150 lines of infrastructure code. For a project with 30 entities, that is 4,500 lines of mechanical, error-prone code that you write, maintain, and keep in sync. Every new property means updating the entity, the setter, the Create factory, the configuration, the DTO mapping, and possibly the query filter.

**The fundamental issue**: the framework has enough information from your entity declaration to generate all of this. Attributes describe intent. The rest is derivable.

---

## The Solution

Pragmatic.Persistence inverts the model. You declare the entity's **shape** and **intent** with attributes. The source generator reads those declarations at compile time and produces every artifact: identity properties, audit fields, soft-delete fields, the Create factory, typed setters, the repository class, EF Core entity configuration, query filters, DI registration, and mutation pipelines.

The same Order entity:

```csharp
[Entity]
[Auditable]
[SoftDelete]
[BelongsTo<SalesBoundary>]
[Relation.OneToMany<LineItem>]
[Relation.ManyToOne<Customer>]
public partial class Order
{
    [LogicKey]
    public string OrderNumber { get; private set; } = "";

    public decimal Total { get; private set; }
}
```

The same query:

```csharp
[Query<Order, OrderDto>]
[Endpoint(HttpVerb.Get, "api/v1/orders/search")]
public partial class SearchOrdersQuery
{
    [Filter(Operator = FilterOperator.Contains, MapTo = "Customer.Name")]
    public string? CustomerName { get; init; }

    [Filter]
    public OrderStatus? Status { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? CreatedAtSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

The source generator produces:
- `PersistenceId`, `Id`, and the trait interfaces (`IAuditable`, `ISoftDelete`, `IAuditedEntity`)
- `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` (from `[Auditable]`)
- `IsDeleted`, `DeletedAt`, `DeletedBy` (from `[SoftDelete]`)
- `static Create(...)` factory with all required properties
- `SetOrderNumber(string)`, `SetTotal(decimal)` typed setters
- `CustomerId` FK property, `Customer` navigation, `Items` collection (from `[Relation.*]`)
- `Order.Repository` nested class implementing `IRepository<Order>` with `GetByOrderNumberAsync`
- `EntityConfig.Order.g.cs` with key, indexes, relationships, query filter
- `Order.SoftDeleteFilter` implementing `IQueryFilter<Order>`
- `SearchOrdersQuery.Apply()` with generated WHERE + ORDER BY
- DI registration for repository, DbContext, query filters

150 lines of infrastructure code reduced to 20 lines of declarations. No reflection at runtime. The generated code is visible under `obj/` in your IDE, fully debuggable.

---

## How It Works: Entity Lifecycle

Every entity in Pragmatic goes through a deterministic lifecycle, from declaration to database. The source generator assembles the lifecycle at compile time based on the attributes you choose.

```
Your Entity Class (attributes)
  |
  v
Pragmatic.SourceGenerator (compile time)
  |
  +---> Entity Members       PersistenceId, Id, Create(), SetXxx()
  +---> Trait Properties     IAuditable, ISoftDelete, IConcurrencyAware
  +---> Relationship Props   FK properties, navigation properties
  +---> Repository Class     Nested Entity.Repository : IRepository<T>
  +---> Entity Config        IEntityTypeConfiguration<T> for EF Core
  +---> Query Filters        SoftDeleteFilter, TenantFilter, TemporalFilter
  +---> DI Registration      Extension methods for service collection
  |
  v
Pragmatic.Persistence.EFCore (runtime)
  |
  +---> DbContext             Boundary-scoped, with generated DbSets
  +---> Interceptors          AuditingInterceptor, SoftDeleteInterceptor
  +---> Query Executor        EfCoreQueryExecutor with filter pipeline
  +---> Filter Pipeline       Root + navigation filtering
  |
  v
Database (SQL)
```

The lifecycle steps that apply depend on the attributes present on the entity. An entity with only `[Entity]` gets the minimum: identity, factory, setters, repository. Add `[Auditable]` and it gains four audit fields plus the interceptor wiring. Add `[SoftDelete]` and it gains three soft-delete fields, a query filter, and modified Remove behavior. The SG only generates what is declared -- there is no performance penalty for unused features.

---

## Entity System

The entity system is the foundation. Everything else -- repositories, queries, mutations, filters -- builds on entity declarations.

### Core Declaration

Every entity starts with `[Entity]` on a `partial class`:

```csharp
[Entity]
[BelongsTo<SalesBoundary>]
public partial class Order
{
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

The class must be `partial` because the SG emits additional members in a separate `.g.cs` file. Without `partial`, the compiler cannot merge them, and diagnostic `PRAG0600` fires.

### The key

The key is a `Guid`, always: a version 7 value, time-ordered and index-friendly, assigned at
construction without a database round-trip. There is no type argument to pick. An identifier that
comes from elsewhere is an ordinary property with its own uniqueness (`[LogicKey]`,
`[Unique(nameof(...))]`); see [02-entity-system.md](/modules/persistence/02-entity-system/).

### Generated Members

From `[Entity]` alone, the SG produces:

| Member | Purpose |
|--------|---------|
| `Guid PersistenceId { get; set; }` | The primary key. |
| `Guid Id => PersistenceId` | Typed alias for convenience. |
| `static Create(...)` | Factory method with parameters for all settable properties. |
| `SetXxx(value)` | `internal` typed setter for each `private set` property, with change tracking. |

⚠️ **Equality is not generated.** An entity is a class and compares by reference: two instances loaded
from the same row through different contexts are not equal, and `Contains`/`Distinct` over entities do
not do what the id would suggest. Compare `PersistenceId`, or write the members yourself; nothing in
the generated code will collide with them.

### Entity Traits

Traits are attributes that add cross-cutting fields and behavior to entities. Each trait adds properties, an interface implementation, and infrastructure hooks.

| Trait | Interface | Added Properties | Runtime Behavior |
|-------|-----------|-----------------|-----------------|
| `[Auditable]` | `IAuditable` | `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` | `AuditingInterceptor` sets values on save |
| `[SoftDelete]` | `ISoftDelete` | `IsDeleted`, `DeletedAt`, `DeletedBy` | `Remove()` sets fields instead of DELETE. Query filter hides deleted rows. |
| `[SoftDelete(Cascade = true)]` | `ISoftDelete` | Same as above | Soft-deleting parent cascades to children |
| `[ConcurrencyAware]` | n/a | **none** | The token is an EF Core *shadow property*, declared in the generated DbContext and shaped by the provider (`byte[]` rowversion on SQL Server, `uint` mapped to `xmin` on PostgreSQL). The generated repository's `SaveChangesAsync` then returns `Result<int, ConcurrencyError>` instead of `Task<int>`. |

Traits compose freely. An entity can have all of them:

```csharp
[Entity]
[Auditable]
[SoftDelete]
[ConcurrencyAware]
[BelongsTo<BillingBoundary>]
public partial class Invoice
{
    public decimal Total { get; private set; }
}
```

### PersistenceId vs Business Keys

Pragmatic separates the technical identifier (`PersistenceId`) from human-facing business keys. Use `[LogicKey]` for business identifiers:

```csharp
[Entity]
public partial class Product
{
    [LogicKey]
    public string Sku { get; private set; } = "";  // Business key: unique index generated
    public string Name { get; private set; } = "";
}
```

`[LogicKey]` generates a unique index in EF Core configuration and a `GetBySkuAsync()` method on the concrete repository. `[GeneratedValue("ORD-{YYYY}{MM}-{SEQ:5}")]` generates formatted business keys with date and sequence placeholders, under a create mutation only, and with the gaps a database sequence leaves. Both limits are spelled out in [Entity System](/modules/persistence/02-entity-system/#generatedvalue).

### State Machines

Entities with status fields use `[StateMachine<TEnum>]` for validated transitions:

```csharp
public enum OrderStatus
{
    [InitialState]
    Draft,
    [TransitionFrom(OrderStatus.Draft)]
    Pending,
    [TransitionFrom(OrderStatus.Pending)]
    Approved,
    [TransitionFrom(OrderStatus.Approved)]
    Shipped,
    [TransitionFrom(OrderStatus.Shipped)]
    Delivered,
    [TransitionFrom(OrderStatus.Pending), TransitionFrom(OrderStatus.Approved)]
    Cancelled
}

[Entity]
[StateMachine<OrderStatus>]
public partial class Order
{
    public OrderStatus Status { get; private set; }   // yours to declare
}
```

⚠️ **The argument must be the enum value, not its name.** `TransitionFromAttribute` takes an `object`,
so `[TransitionFrom(nameof(Draft))]` compiles, and the generator ignores it, because it reads the
argument only when its type is the enum. The state then has no incoming transition at all, which
surfaces as **PRAG0621** rather than as the transition you thought you declared.

The SG generates `TransitionTo(target)` returning `VoidResult<IError>` (a refusal is a
`Pragmatic.Result.Http.ConflictError`, status 409, naming both states), plus `CanTransitionTo(target)`
and `AllowedTransitions()`. Invalid transitions never throw. Full guide:
[State Machine](/modules/persistence/19-state-machine/).

---

## Relationships

All relationships are declared via `[Relation.*]` attributes on the entity class. The SG generates FK properties, navigation properties, and EF Core configuration.

### Relationship Types

```csharp
// One-to-many: parent side
[Relation.OneToMany<LineItem>]

// Many-to-one: child side
[Relation.ManyToOne<Customer>]

// One-to-one
[Relation.OneToOne<UserProfile>]

// Many-to-many with explicit join entity: a second type argument, never a typeof
[Relation.ManyToMany<Tag, OrderTag>]

// Explicit navigation names
[Relation.ManyToOne<User>.WithNavigation("CreatedBy", Inverse = "CreatedOrders")]
```

### What Gets Generated

For `[Relation.OneToMany<LineItem>]` on `Order`:
- On `Order`: `public ICollection<LineItem> LineItems { get; set; }`, named after the **target type**,
  pluralised, not after any property you wrote. `.WithNavigation("Items")` renames it
- On `LineItem`: `public Guid OrderId { get; private set; }` (FK) and `internal void SetOrderId(...)`
- EF Core config: `HasMany` / `WithOne` / `OnDelete(Cascade)`; see below, the default is not the
  cautious one

### Cross-Boundary Relationships

Entities from different boundaries can reference each other, but with limitations. The FK property is generated and stored. Navigation properties are **not** generated because the entities live in different DbContexts. `Include()` across boundaries does not work. Load related data separately instead.

Nothing flags a cross-boundary relation at compile time: the FK is generated and the navigation is not, so plan for FK-only access from the start.

### Delete Behavior

The delete behaviour follows **which side declares the relation**: `[Relation.OneToMany]` on the parent gives `Cascade`, `[Relation.ManyToOne]` on the child gives `Restrict`. See [Relationships](/modules/persistence/04-relationships/#delete-behavior): the default is not the cautious one, and `[SoftDelete]` does not change the foreign key.

---

## Boundaries and DbContext

A boundary is a marker class that groups entities into a logical domain partition. Each boundary maps to a DbContext, a keyed `IUnitOfWork`, and a set of repositories.

```csharp
// Define boundaries
public class BillingBoundary;
public class BookingBoundary;

// Assign entities
[Entity]
[BelongsTo<BillingBoundary>]
public partial class Invoice { /* ... */ }

[Entity]
[BelongsTo<BookingBoundary>]
public partial class Reservation { /* ... */ }
```

### Why Boundaries Exist

| Without Boundaries | With Boundaries |
|-------------------|-----------------|
| One giant DbContext with all entities | Focused DbContexts per domain (10-30 entities each) |
| Slow model building at startup | Fast model building |
| No isolation between domains | Clear ownership and access control |
| All entities share one connection string | Different boundaries can use different databases |
| One transaction for everything | Boundary = unit of transactional consistency |

### DbContext Generation

The host project declares the database provider:

```csharp
// In the host: declare the database…
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Sales")]
public sealed class SalesDatabase : PragmaticDatabase;

// …and say which module lives on it. That pairing IS the topology.
[Include<SalesModule, SalesDatabase>]
public sealed class AppHostModule;
```

You never write a `DbContext`: the generator emits one per boundary from that pairing, and the
generated host registers each of them (`AddCatalogDbContext(...)`, `AddBillingDbContext(...)`) with
the options built from the database's `ConfigKey`. `AddAllPragmaticDbContexts(o => …)` is the same set
behind one call, for a project that wires them by hand.

Two modules on one database share a connection; two databases mean two. The number of `DbContext`
types follows from that declaration and from nothing else.

### UnitOfWork

`IUnitOfWork` is registered as a keyed service by boundary type:

```csharp
var uow = services.GetRequiredKeyedService<IUnitOfWork>(typeof(BillingBoundary));
await uow.SaveChangesAsync(ct);
```

Transactions are explicit:

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

---

## Repository Pattern

The SG generates a concrete repository class for each entity as a **nested class** inside the entity's partial class. This keeps the repository co-located with its entity.

### Two API Layers

There are two repository surfaces:

**1. Stable abstractions** -- the interfaces you should prefer for application code:

```csharp
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

public interface IRepository<TEntity> : IReadRepository<TEntity>
{
    void Add(TEntity entity);
    void AddRange(IEnumerable<TEntity> entities);
    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
    void Update(TEntity entity);
}
```

**2. Generated concrete repository** -- exposes additional convenience members:

```csharp
public partial class Order
{
    public partial class Repository : IRepository<Order>
    {
        // All IRepository methods, plus:

        public DbSet<Order> Set { get; }

        // Logic key lookup (from [LogicKey])
        public Task<Order?> GetByOrderNumberAsync(string key, CancellationToken ct = default);

        // Include overload
        public Task<Order?> GetByIdAsync(
            Guid id, Func<IQueryable<Order>, IQueryable<Order>> includes,
            CancellationToken ct = default);

        // Strategy overload: Projection, Entity, Filtered, Raw
        public IQueryable<Order> Query(QueryStrategy strategy);

        // Bulk operations: on every entity, not only some
        public Task<int> BulkInsertAsync(IReadOnlyList<Order> entities,
                                         BulkInsertOptions? options = null, CancellationToken ct = default);
        public Task<int> BulkUpsertAsync(IReadOnlyList<Order> entities,
                                         UpsertOptions? options = null, CancellationToken ct = default);
        public Task<int> BulkUpdateAsync(ISpecification<Order> filter,
                                         Action<UpdateSettersBuilder<Order>> update, CancellationToken ct = default);
        public Task<int> BulkDeleteAsync(ISpecification<Order> filter, CancellationToken ct = default);
        public Task<int> UpsertAsync(Order entity, UpsertMatch matchOn = UpsertMatch.PrimaryKey,
                                     CancellationToken ct = default);

        public Task<int> SaveChangesAsync(CancellationToken ct = default);
    }
}
```

### Which One to Inject?

| Need | Inject |
|------|--------|
| Stable CRUD / specification access | `IRepository<Order>` |
| Read-only stable access | `IReadRepository<Order>` |
| Logic key helper or include overload | `Order.Repository` (concrete) |
| Bulk operations | `Order.Repository` (concrete) |

### Specifications

Repositories accept `Specification<T>` for reusable, composable predicates:

```csharp
var largeOrders = Spec<Order>.Where(o => o.Total > 1000);
var pendingLarge = largeOrders & Spec<Order>.Where(o => o.Status == OrderStatus.Pending);

var count = await orders.CountAsync(largeOrders, ct);
var list = await orders.FindAsync(pendingLarge, ct);
```

Specifications support `&` (AND), `|` (OR), `!` (NOT), and conditional composition with `AndIf` / `OrIf`.

### Automatic Filtering

Every generated read path applies root filters automatically through `IQueryFilterProvider`. Soft-deleted rows are excluded, tenant data is isolated, and temporal filters are applied -- all without you writing `Where(!IsDeleted)` on every query.

---

## Query System

The query system lets you declare what to filter, sort, and project -- the SG produces the implementation.

### Declarative Queries

```csharp
[Query<Order, OrderDto>]
public partial class SearchOrders
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? CustomerName { get; init; }

    [Filter]
    public OrderStatus? Status { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]   // Descending by default
    public SortDirection? CreatedAtSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

The SG generates:
- `IPagedQuery<Order, OrderDto>` implementation
- `Apply(IQueryable<Order>)` with WHERE + ORDER BY
- a `Projection` that comes from **`[GenerateProjection]`** on the DTO; `[MapFrom<Order>]` alone gives
  `FromEntity` and `Selector`, and a query whose DTO lacks `[GenerateProjection]` is **PRAG0704**

### Filter Operators

| Operator | SQL Equivalent | Typical Use |
|----------|---------------|-------------|
| `Equals` | `= value` | Exact match (ID, status, boolean) |
| `NotEquals` | `!= value` | Exclusion filter |
| `Contains` | `LIKE '%value%'` | Text search |
| `StartsWith` | `LIKE 'value%'` | Prefix search |
| `GreaterThan` / `GreaterOrEqual` | `>` / `>=` | Range (dates, amounts) |
| `LessThan` / `LessOrEqual` | `<` / `<=` | Range |
| `In` | `IN (values)` | Multi-select (statuses, categories) |

⚠️ `FilterOperator.Between` is declared and no generator renders it, so declaring it is **PRAG0701**,
an error; see [Query System](/modules/persistence/09-query-system/#available-operators). Express a range with two
properties sharing one `MapTo`.

### Filter DTOs

Reusable filter logic without a full query:

```csharp
[FilterDto<Order>]
public partial class OrderFilter
{
    [Filter]
    public OrderStatus? Status { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Total")]
    public decimal? MinTotal { get; init; }
}

// Usage: var pending = await db.Orders.ApplyFilter(filter).ToListAsync(ct);
```

### Query Execution Pipeline

```
HTTP GET /api/orders/search?Status=Pending&Page=2
  |
  v
1. Model binding --> Query object
2. IQueryExecutor.ExecuteAsync()
3. Prepare source (NoTracking, SplitQuery, IgnoreGlobalFilters)
4. Apply global filters (soft-delete, tenant, permission)
5. query.Apply() -- YOUR generated WHERE + ORDER BY
6. COUNT(*), SKIP/TAKE, SELECT projection
7. Return PagedResult<TResult>
  |
  v
HTTP 200 { items: [...], totalCount: 47, page: 2, totalPages: 3 }
```

All filters -- root, navigation, and query -- compose into a single expression tree before EF Core translates to SQL. There is no in-memory filtering.

---

## Query Filters

Query filters are safety rails that enforce data visibility rules. You define them once; they apply automatically on every generated read path.

### Auto-Generated Filters

| Source | Effect |
|--------|--------|
| `[SoftDelete]` on entity | Hides rows where `IsDeleted == true` |
| Tenant-aware entity (implements `ITenantEntity`) | Restricts rows to `TenantId == currentTenant` |
| `[TemporalRelation]` on entity | Hides inactive historical rows |

### Filter Modes

| Mode | Soft-delete | Tenant | Data scope | Visibility | Permission |
|------|---|---|---|---|---|
| **Normal** (0) | Applied | Applied | Applied | Applied | Applied |
| **Admin** (1) | Applied | Applied | Applied | Skipped | Skipped |
| **Elevated** (2) | Applied | Applied | Skipped | Skipped | Skipped |
| **Background** (3) | Applied | Skipped | Skipped | Skipped | Skipped |
| **Raw** (4) | Skipped | Skipped | Skipped | Skipped | Skipped |

### Bypassing Filters

Use `IQueryFilterToggle` for scoped overrides:

```csharp
// Disable a specific filter
using (filterToggle.Disable<Order.SoftDeleteFilter>())
{
    var allOrders = await orders.Query().ToListAsync(ct);  // Includes deleted
}

// Switch to admin mode
using (filterToggle.UseMode(FilterMode.Admin))
{
    // Skip visibility and permission filters
}
```

The override is temporary -- `AsyncLocal`-scoped, restored automatically on dispose.

---

## Mutations

Mutations are operations that modify a single entity through the MutationInvoker pipeline. A mutation class inherits from `Mutation<TEntity>` and declares properties to map.

### Mutation Modes

| Mode | Behavior |
|------|----------|
| `Create` | `new TEntity()` (**not** the generated `Create()` factory), then computed defaults, `OnCreating`, the mapped properties, and the save |
| `Update` | Loads entity by ID, applies non-null properties (partial update), saves |
| `Delete` | Loads entity, marks deleted (soft-delete if `[SoftDelete]`), saves |
| `Restore` | Loads bypassing all filters, resets `IsDeleted`/`DeletedAt`/`DeletedBy`, saves |

### The Mutation Pipeline

```
HTTP POST /api/v1/invoices
  |
  v
1. Endpoint handler (model binding, pre-processors)
2. MutationInvoker.InvokeAsync():
   a. Inject dependencies
   b. L1 Validation: sync (attribute-based) + async (DB-aware)
   c. Load or create entity
   d. Apply computed defaults (if create)
   e. mutation.ApplyAsync(entity) -- generated property mapping
   f. L2 Validation: entity invariants (after apply)
   g. Persist (repository.Add + UnitOfWork.SaveChanges)
   h. Dispatch domain events
   i. Invalidate cache
  |
  v
Result<TEntity, IError>
  |
  v
HTTP 201 Created / 400 / 404
```

### Why Two Validation Levels?

| Level | Target | When | What It Catches |
|-------|--------|------|-----------------|
| L1 | Mutation DTO | Before entity load | Invalid input (missing fields, bad format, non-existent references) |
| L2 | Entity | After apply | Business invariants (negative balance, invalid state transition) |

L1 is fast and prevents unnecessary database work. L2 catches violations that only become apparent after the mutation is applied to the entity.

### Writing an aggregate's children

A mutation can carry its aggregate's children. The child has to declare `[PartOf<TParent>]` (fail-closed,
because no relation metadata separates a line item from a room type), the mutation property has to be
named after the navigation it writes, and the elements have to be matchable by `Id` or by the child's
`[LogicKey]`. Each of the three has a diagnostic when it does not hold: **PRAG0436**, **PRAG0439**,
**PRAG0333**.

The strategy is **derived** rather than declared: a `[Patch]` is a partial representation so it adds and
updates, anything else is a full one so a child that was not sent is a child you are saying is not there.

| `CollectionStrategy` | Behaviour |
|---|---|
| `Sync` | Match by key: update, add, **remove what was not sent**; the default for a mutation |
| `AddOnly` | Match by key: update and add, remove nothing; the default under `[Patch]` |
| `Replace` | Discard every child and rebuild: new rows, new identities |
| `Ignore` | Leave the collection alone |

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class CurateOrderLinesMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    [CollectionStrategy(CollectionStrategy.AddOnly)]   // only where the default reads it wrong
    public required List<OrderLineDto> OrderLines { get; init; }
}
```

Full treatment in [06-mutations](/modules/persistence/06-mutations/).

---

## What Gets Generated

For each entity marked with `[Entity]`, the SG produces files depending on the attributes present. Here is the complete table:

Every per-type file carries the entity's namespace, so two entities with the same simple name in
different namespaces cannot produce the same file name:

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `{Ns}.{Entity}.Traits.g.cs` | `PersistenceId`, `Id`, **and** the `[Auditable]` / `[SoftDelete]` properties: one file for all of them | Always |
| `{Ns}.{Entity}.Create.g.cs` | Static `Create(...)` factory | Non-abstract entities |
| `{Ns}.{Entity}.Setters.g.cs` | `internal Set{Property}(...)` with change tracking | Has `private set` properties |
| `{Ns}.{Entity}.Specs.g.cs` | `{Entity}Specifications.ById(...)` and `By{LogicKey}(...)` | Always |
| `{Ns}.{Entity}.Relations.g.cs` | FK properties, navigation properties | `[Relation.*]` |
| `{Ns}.{Entity}.Repository.g.cs` | Nested `{Entity}.Repository` | Has `[BelongsTo]` + EFCore ref |
| `{Ns}.{Entity}.SoftDeleteFilter.g.cs` | Nested `{Entity}.SoftDeleteFilter` | `[SoftDelete]` |
| `{Ns}.{Entity}.TenantFilter.g.cs` | Nested `{Entity}.TenantFilter` | Implements `ITenantEntity` |
| `{Ns}.{Entity}.StateMachine.g.cs` | `TransitionTo()`, `CanTransitionTo()`, `AllowedTransitions()` | `[StateMachine<TEnum>]` |
| `{Ns}.{Entity}.Projectable.g.cs` | The nested `{Entity}.Expr` class of expressions | `[Projectable]` |
| `{Ns}.{Entity}.ComputedFilter.g.cs` | `{Entity}ComputedFilters`: `{Prop}Spec` fields and `Where{Prop}()` | `[ComputedFilter]` |
| `EntityConfig.{Ns}.{Entity}.g.cs` | EF Core `IEntityTypeConfiguration` (host-level) | Has `[BelongsTo]` + EFCore ref |
| `_Infra.Persistence.RepositoryRegistration.g.cs` and friends | DI registration: repositories, DbContexts, query filters, filter map | Once per assembly |
| `DbContext.{Boundary}.g.cs` | Boundary DbContext with DbSets | Per boundary |

All generated files live under `obj/Debug/net10.0/generated/` and are fully visible in the IDE. You can set breakpoints in generated code.

---

## Choosing the Right Pattern

### When to Use What

| Scenario | Pattern | Why |
|----------|---------|-----|
| Simple read by ID | `IReadRepository<T>.GetByIdAsync()` | Direct, no overhead |
| Read by business key | `Entity.Repository.GetByXxxAsync()` | Concrete repo has the helper |
| Paginated search with filters | `[Query<T, R>]` with `[Filter]` / `[Sort]` | SG generates the pipeline |
| Grid with dynamic operators | `[GridFilter<T>]` with `[Filterable]` | Runtime operator selection |
| External grid framework (DevExpress, PrimeNG) | `QueryBuilder.FromDevExpress()` / `FromPrimeNG()` | Runtime adapter |
| Reusable filter logic | `[FilterDto<T>]` | Shared across queries |
| Create entity from DTO | `Mutation<T>` with `MutationMode.Create` | Generated load-apply-save |
| Update entity (partial) | `Mutation<T>` with `MutationMode.Update` | Nullable props = skip unchanged |
| Delete (soft) | `Mutation<T>` with `MutationMode.Delete` | Auto soft-delete for `[SoftDelete]` entities |
| Complex business operation | `DomainAction<T>` | Full control via `Execute()` |
| Computed property in SQL | `[Projectable]` | Expression for EF Core translation |
| Reusable boolean filter | `[ComputedFilter]` | Expression + Specification + extension |
| Aggregation report | `[QueryView<T>]` | Declarative GROUP BY + SUM/COUNT |

### Projection vs Entity Loading

| Approach | Tracking | Performance | Use When |
|----------|----------|-------------|----------|
| `[Query<T, R>]` (with projection) | No | Best | Read-only endpoints, API responses |
| `[Query<T>]` (entity) | Configurable | Good | Need full entity for business logic |
| `[QueryStrategy(Strategy = QueryStrategy.Raw)]` | No | Fastest | Admin dashboards, bypassing all filters |
| Mutation (entity) | Yes | Standard | Need to modify and save |

**Always prefer projections for read endpoints.** They fetch only the columns the DTO needs, skip change tracking, and avoid materializing navigations you do not use.

---

## EF Core Integration

### Packages

| Package | Role |
|---------|------|
| `Pragmatic.Persistence` | Attributes, interfaces, query/filter primitives |
| `Pragmatic.Persistence.EFCore` | EF Core runtime: DbContext, interceptors, query executor |
| `Pragmatic.SourceGenerator` | Compile-time: generates all `.g.cs` files |

### Interceptors

Registered by the generated `Add{Boundary}DbContext(...)`, each one only when the boundary contains an
entity that needs it:

| Interceptor | Trigger | What It Does |
|-------------|---------|--------------|
| `AuditingInterceptor` | `[Auditable]` | Sets `CreatedAt`/`CreatedBy`/`UpdatedAt`/`UpdatedBy` on `SaveChanges` |
| `SoftDeleteInterceptor` | `[SoftDelete]` | Turns a delete into a flag at save time, on every path |
| `TenantInterceptor` | `ITenantEntity` | Stamps `TenantId` on insert, never on update |
| `OwnershipInterceptor` | `[HasOwner]` | Stamps `OwnerId` from the current user |
| `AuditLogInterceptor` | `[Audited]` | Writes the append-only `__AuditLog` row, in the same transaction |
| `RollUpInterceptor` | `[RollUp<T>]` | Keeps the parent's stored aggregate current |

There is no id interceptor: a `Guid` key is assigned by the trait at construction.

### Global Query Filters

EF Core global query filters serve as a safety net for raw `IQueryable` access. They are registered in the generated entity configuration:

```csharp
// Generated in EntityConfig.{Namespace}.Order.g.cs
builder.HasQueryFilter("SoftDelete", e => !e.IsDeleted);
```

It is a **named** filter, so `IgnoreQueryFilters(["SoftDelete"])` lifts that one without lifting the
rest.

The Pragmatic filter pipeline (via `IQueryFilterProvider`) handles the primary filtering. EF Core global filters are the fallback for code paths that bypass the repository.

### Migrations

There are no migration files. `Pragmatic.Migrations` reads the generated schema, introspects the
database at startup, and applies the difference as idempotent provider-specific SQL in one
transaction:

```csharp
await PragmaticApp.RunAsync(args, builder => builder.UsePragmaticMigrations());
```

The migration context is **per database**, not per boundary: boundaries sharing a connection share
the schema. See the [migrations guide](/modules/migrations/overview/).

---

## Ecosystem Integration

Pragmatic.Persistence is the data layer. Other Pragmatic modules plug into it for deeper integration. Each integration is opt-in: reference the package, and the SG detects it.

### Actions

When a class inherits `Mutation<TEntity>` and has `[Mutation]` + `[Endpoint]`, the SG generates a `MutationInvoker` that handles the full load-validate-apply-persist pipeline. `DomainAction<T>` endpoints can inject `IRepository<T>` for custom business logic.

### Endpoints

`[Query<T, R>]` + `[Endpoint]` generates a complete search API endpoint. `[Mutation]` + `[Endpoint]`
generates CRUD endpoints. The binding is **generated, not ASP.NET's**: the endpoint is a
`RequestDelegate` that reads each parameter and parses it, which is what makes it AOT-safe, and why a
malformed value comes back as a 400 naming the parameter.

### Mapping

`[MapFrom<TEntity>]` on a DTO generates an `Expression<Func<TEntity, TDto>>` used as the projection in `[Query<T, R>]`. Only the columns needed by the DTO are fetched from the database.

### Validation

`[Validate]` on a mutation triggers the two-level validation pipeline. Sync validators run attribute-based checks (`[Required]`, `[Range]`). Async validators perform database-aware checks (uniqueness, existence).

### Events

Entities implementing `IHasDomainEvents` dispatch events after `SaveChanges`. State machine transitions can raise events via `[RaisesEvent<T>]` on enum values. Events are dispatched through `IDomainEventDispatcher`.

### Caching

`[Cacheable]` on a query caches `PagedResult<T>` in `HybridCache`. `[Lookup]` on an entity loads all records into memory at startup for synchronous access.

---

## Advanced Features

### Temporal Relations

Track validity periods (`ValidFrom` / `ValidTo`) with `[TemporalRelation<TParent>]`. The SG generates constraint validation, auto-close of previous records, and a temporal query filter.

### Inheritance (TPH / TPT / TPC)

`[Inheritance(InheritanceStrategy.Tph)]` on a base entity generates discriminator configuration. Derived entities get their own `Create()` factories.

### Hierarchy (Self-Referencing Trees)

`[GenerateHierarchy]` reads the declared self-relation (`[Relation.ManyToOne<TSelf>]`, `Required = false`)
and generates `GetDescendantsBy{Nav}()` and `GetAncestorsBy{Nav}()` using SQL CTEs for recursive queries.

### Projectable and ComputedFilter

`[Projectable]` on an expression-bodied property generates `{Entity}.Expr.{Property}`, an
`Expression<Func<T, TResult>>` of **any** result type, usable in `Where`/`Select`/`OrderBy`.
`[ComputedFilter]` is the `bool`-only superset: it implies `[Projectable]` and adds a `{Prop}Spec`
specification and a `Where{Prop}()` extension.

### Lifecycle Hooks

`IEntityLifecycle<T>` provides `OnCreating` and `OnSaving` hooks for computed defaults and cross-field consistency checks.

### Presets

`[HasPresets]` + `[PresetProvider<T>]` creates child entities automatically when the parent is created.

---

## See Also

- [Getting Started](/modules/persistence/01-getting-started/) -- Install and declare your first entity
- [Entity System](/modules/persistence/02-entity-system/) -- Entity declaration, ID types, generated members
- [Entity Attributes](/modules/persistence/03-attributes/) -- Auditable, SoftDelete, ConcurrencyAware, Lookup, StateMachine
- [Relationships](/modules/persistence/04-relationships/) -- One-to-many, many-to-one, many-to-many, cross-boundary
- [Repository](/modules/persistence/05-repository/) -- CRUD, specifications, include overloads, bulk operations
- [Mutations](/modules/persistence/06-mutations/) -- Create/Update/Delete, collection strategies, nested mutations
- [Query Filters](/modules/persistence/07-query-filters/) -- Soft-delete, tenant, permission, filter toggle
- [Query System](/modules/persistence/09-query-system/) -- Declarative queries, filters, sorts, joins
- [Grid Filtering](/modules/persistence/10-grid-filtering/) -- GridFilter, GridAdapter, DevExpress/PrimeNG integration
- [Projections and Views](/modules/persistence/11-projections-views/) -- Projectable, ComputedFilter, QueryView
- [Query Pipeline](/modules/persistence/15-query-pipeline/) -- Full pipeline from HTTP to SQL
- [Mutation Pipeline](/modules/persistence/16-mutation-pipeline/) -- Full pipeline from HTTP to SaveChanges
- [Boundaries](/modules/persistence/17-boundaries/) -- Logical partitions, DbContext grouping, transaction scope
- [Diagnostics](/modules/persistence/14-diagnostics/) -- Understanding PRAG06xx diagnostics
- [Common Mistakes](/modules/persistence/common-mistakes/) -- Wrong code, right code for the most common pitfalls
- [Troubleshooting](/modules/persistence/troubleshooting/) -- Checklists for generation, DI, filter, and query issues
