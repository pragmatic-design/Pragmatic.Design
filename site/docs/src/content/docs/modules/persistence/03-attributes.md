---
title: "Entity Attributes"
description: "Each attribute you place on an entity tells the source generator to produce specific infrastructure code. This page explains the **problem** each attribute solv"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/03-attributes.md
sidebar:
  order: 4
---
Each attribute you place on an entity tells the source generator to produce specific infrastructure code. This page explains the **problem** each attribute solves, what it **generates**, and **when** to use it.

---

## `[Auditable]`

### Problem

You need to know who created or modified a record and when. This is required for compliance (GDPR, SOX), debugging production issues, and building audit trails. Writing these fields manually on every entity is tedious and easy to forget.

### Solution

Add `[Auditable]` to your entity. The source generator adds four properties and the `IAuditable` interface:

```csharp
// ═══ What YOU write ═══
[Entity]
[Auditable]
public partial class Order
{
    public string OrderNumber { get; private set; } = "";
}
```

```csharp
// ═══ What the SG generates ═══
public partial class Order : IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
```

### How the values are set

The audit fields are **not** set by your code: they are populated automatically by the `AuditingInterceptor` in `Pragmatic.Persistence.EFCore`:

| When | CreatedAt | CreatedBy | UpdatedAt | UpdatedBy |
|------|-----------|-----------|-----------|-----------|
| Entity is first saved | Set to `now` | Set from `ICurrentUser` | Set to `now` | Set from `ICurrentUser` |
| Entity is updated | Preserved | Preserved | Updated to `now` | Updated from `ICurrentUser` |

If `ICurrentUser` is not registered in DI, the `*By` fields remain `null`.

---

## `[Audited]`

### Problem

`[Auditable]` stamps *who/when* on the row itself, but it only tells you the **last** change. For
compliance and troubleshooting you often need the full **history** of changes (an append-only log of
what happened, by whom, when) without converting the model to event sourcing.

### Solution

Mark the entity `[Audited]`. The source generator makes it implement `IAuditedEntity`, and the
`AuditLogInterceptor` (auto-registered in the boundary's DbContext) writes one append-only row to the
`__AuditLog` table for **every** insert, update and delete, **in the same transaction** as the change,
so the trail can never diverge from what was persisted.

```csharp
[Entity]
[Audited]
public partial class Amenity : IEntity
{
    public string Name { get; private set; } = "";
}
```

Each `AuditLogEntry` captures: `EntityType`, `EntityId`, `Action` (`Created`/`Updated`/`Deleted`),
`UserId` (from `ICurrentUser`), `OccurredAt` (UTC), and `CorrelationId` (the ambient W3C trace id).

### What happens automatically

- The `__AuditLog` table is emitted into the migration schema (SchemaMetadata) **and** mapped in the
  boundary DbContext; the two stay in sync (dual-source).
- The `AuditLogInterceptor` is registered for any boundary that owns at least one `[Audited]` entity.

**`[Audited]` vs `[Auditable]`**: `[Auditable]` = last-change stamp on the row; `[Audited]` = full
append-only change log in a separate table ("event-sourcing-light"). They compose: use both when you
want both the current stamp and the history.

---

## `[ValueObject]`

### Problem

Value objects (Money, Address, ContactInfo…) have no identity and are compared by value. Hand-writing
equality, validated construction, and the EF mapping for every one is boilerplate.

### Solution

Mark the value object `[ValueObject]` (a `partial record` with a `private static Validate`). The SG
generates `Create(...)` and `CreateUnsafe(...)`, and **`Create` mirrors whatever `Validate` returns**:
return the value object and `Create` returns it; return `Result<T, ValidationError>` and so does
`Create`. The example below takes the first form. When the VO is
used as an **entity property**, the SG also maps it as an EF Core **complex type**, flattening it into
`{Property}_{Sub}` columns in both the entity configuration **and** the migration schema (dual-source).

```csharp
[ValueObject]
public partial record ContactInfo
{
    public ContactInfo(string email, string phone) { Email = email; Phone = phone; }
    public string Email { get; init; } = "";   // init (not get-only) so EF binds the complex type
    public string Phone { get; init; } = "";
    private static ContactInfo Validate(string email, string phone) => new(email, phone);
}

[Entity]
public partial class Amenity : IEntity
{
    public ContactInfo Support { get; private set; } = new("", "");  // → Support_Email, Support_Phone
}
```

> ⚠️ VO sub-properties must be `init`/settable: EF Core needs to bind the constructor parameters to
> mapped properties; get-only properties are not mapped and the complex type fails to materialize.

---

## `[SoftDelete]`

### Problem

Deleting data from a database is irreversible. In many applications, you need the ability to "delete" records while keeping them in the database, for recovery, compliance, or referential integrity.

**Hard delete**: `DELETE FROM Orders WHERE Id = @id`; data is gone forever.
**Soft delete**: `UPDATE Orders SET IsDeleted = 1 WHERE Id = @id`; data is hidden but recoverable.

### Solution

Add `[SoftDelete]` to your entity:

```csharp
// ═══ What YOU write ═══
[Entity]
[SoftDelete]
public partial class Customer
{
    public string Name { get; private set; } = "";
}
```

```csharp
// ═══ What the SG generates ═══
public partial class Customer : ISoftDelete
{
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
```

### What happens automatically

1. **Query filtering**: a nested `Customer.SoftDeleteFilter` is generated and registered, with priority 100 so it runs before the others. Every generated read path excludes rows where `IsDeleted == true`; you see them only by asking.

2. **Repository `Remove()`**: when you call `repository.Remove(customer)`, it performs a soft-delete (sets `IsDeleted = true`) instead of a physical DELETE.

3. **Cascade** (optional): with `[SoftDelete(Cascade = true)]`, soft-deleting a parent also soft-deletes its children:

```csharp
[Entity]
[SoftDelete(Cascade = true)]           // Deleting a Property also soft-deletes its RoomTypes
[Relation.OneToMany<RoomType>]
public partial class Property { /* ... */ }
```

### Bypassing the filter

Sometimes you need to see deleted records (e.g., an admin "recycle bin"):

```csharp
using (filterToggle.Disable<Customer.SoftDeleteFilter>())
{
    var allCustomers = await repo.Query().ToListAsync(ct);  // includes deleted
}
```

⚠️ `IgnoreQueryFilters()` does **not** do this. It removes EF Core's own named filter (the safety net
for code that queries the `DbSet` directly), while the Pragmatic filter is a `Where` the repository
applies on top. Use the toggle above, or `Query(QueryStrategy.Raw)`.

See [Query Filters](/modules/persistence/07-query-filters/) for full details on the filter toggle mechanism.

---

## `[ConcurrencyAware]`

### Problem

Two users open the same entity, edit it, and save. Without concurrency control, the second save silently overwrites the first user's changes. This is called the **lost update problem**.

### Solution

Optimistic concurrency: each entity has a `RowVersion` that the database increments on every update. If you try to save with an outdated version, EF Core throws `DbUpdateConcurrencyException`.

```csharp
// ═══ What YOU write ═══
[Entity]
[ConcurrencyAware]
public partial class Invoice { /* ... */ }
```

**Nothing is added to your entity.** No property, no interface: the token is an EF Core *shadow
property*, declared in the generated DbContext and never visible in your model:

```csharp
// ═══ In DbContext.{Boundary}.g.cs, and it depends on the provider ═══

// SQL Server: native rowversion, maintained by the database
modelBuilder.Entity<Invoice>().Property<byte[]>("RowVersion").IsRowVersion();

// PostgreSQL: Npgsql maps this to the xmin system column, so no migration, no column of your own
modelBuilder.Entity<Invoice>().Property<uint>("RowVersion")
    .IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

// anything else: a portable uint token
modelBuilder.Entity<Invoice>().Property<uint>("RowVersion").IsConcurrencyToken();
```

### How it works at runtime

1. User A loads the invoice, User B loads the same invoice; both hold the same token
2. User A saves → the token moves on
3. User B saves → the `UPDATE ... WHERE RowVersion = @old` matches no row

What reaches your code is **not** an exception. The generated repository's `SaveChangesAsync` changes
shape when the entity is `[ConcurrencyAware]`:

```csharp
// on a [ConcurrencyAware] entity
public async Task<Result<int, ConcurrencyError>> SaveChangesAsync(CancellationToken ct = default);

// on any other entity
public Task<int> SaveChangesAsync(CancellationToken ct = default);
```

So the conflict arrives as a `ConcurrencyError` (`Code = "CONCURRENCY_CONFLICT"`, `StatusCode = 409`)
and you handle it like any other `Result` failure: reload, merge, retry.

⚠️ Saving through `IUnitOfWork.SaveChangesAsync` instead of the repository does not classify it: there
the `DbUpdateConcurrencyException` travels out as-is, deliberately, because a stale row is two writers
meeting rather than a rule the schema enforces.

---

## `[BelongsTo<TBoundary>]`

### Problem

Large applications have many entities. Putting them all in one DbContext becomes unwieldy: slow model building, confusing navigation properties across unrelated domains.

### Solution

Boundaries partition entities into logical groups. Each boundary maps to a DbContext.

```csharp
// Define boundaries as empty marker classes
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

You never write a DbContext. The host declares a database and pairs each module to it, and the
generator emits one DbContext per boundary from that pairing:

```csharp
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;

[Include<BillingModule, AppDatabase>]
public sealed class AppHostModule;
```

See [DbContext Generation](/modules/persistence/efcore-01-dbcontext-generation/) for how boundaries connect to DbContexts.

---

## `[Lookup]`

### Problem

Some entities are reference data that rarely changes: countries, categories, statuses. Querying the database for these on every request wastes resources.

### Solution

`[Lookup]` marks an entity as a lookup table. At application startup, all records are loaded into an in-memory cache. Subsequent access is synchronous and instant, with zero database queries.

```csharp
// ═══ What YOU write ═══
[Entity]
[Lookup]
[BelongsTo<CatalogBoundary>]
public partial class Category
{
    public string Name { get; private set; } = "";
}
```

```csharp
// ═══ What the SG generates ═══

// {Ns}.Category.LookupCacheLoader.g.cs
public sealed class CategoryLookupCacheLoader : ILookupCacheLoader
{
    public Task LoadAsync(IServiceProvider serviceProvider, CancellationToken ct);
}

// _Infra.Persistence.LookupCache.g.cs: one per assembly
public static class CatalogLookupCacheRegistrationExtensions
{
    // registers every ILookupCache<T, TId> as a singleton, every loader, and the hosted service
    public static IServiceCollection AddCatalogLookupCaches(this IServiceCollection services);
}
```

⚠️ **Call it.** `Add{Prefix}LookupCaches()` is generated, not wired; call it from your `IStartupStep`,
as the Showcase does. Skip the call and there is no `ILookupCache<T, TId>` in the container and no
preload: the attribute looks applied and does nothing. `{Prefix}` is the identifier derived from the
common namespace of the lookup entities, so an assembly whose lookups all sit under `Showcase.*`
generates `AddShowcaseLookupCaches()`.

The preload runs in `LookupPreloadHostedService` at startup, sequentially, and **fails fast**: a loader
that throws stops the application rather than leaving a half-filled cache to surface later as scattered
`KeyNotFoundException`s.

### Usage

```csharp
public class ProductService(ILookupCache<Category, Guid> categories)
{
    public Category GetCategory(Guid id)
        => categories.Get(id);              // synchronous, zero DB query; throws if the id is unknown

    public bool TryGetCategory(Guid id, out Category? category)
        => categories.TryGet(id, out category);

    public IReadOnlyList<Category> GetAll()
        => categories.GetAll();             // all categories, from memory
}
```

### When to use

- Reference data that changes infrequently (countries, currencies, categories)
- Data accessed on almost every request (roles, statuses)

### When NOT to use

- Data that changes frequently (you'd need cache invalidation)
- Large datasets (thousands of records in memory)

---

## `[StateMachine<TEnum>]`

### The Problem

Entities with status fields (`Pending → Approved → Shipped → Delivered`) need transition validation. Without it, any code can set `order.Status = Delivered` directly, skipping required steps and violating business rules.

### The Solution

Define allowed transitions on the enum itself:

```csharp
public enum OrderStatus
{
    [InitialState]
    Draft,

    [TransitionFrom(OrderStatus.Draft)]
    Pending,

    [TransitionFrom(OrderStatus.Pending)]
    [RaisesEvent<OrderApprovedEvent>]
    Approved,

    [TransitionFrom(OrderStatus.Approved)]
    Shipped,

    [TransitionFrom(OrderStatus.Shipped)]
    [RaisesEvent<OrderDeliveredEvent>]
    Delivered,

    [TransitionFrom(OrderStatus.Pending)]
    [TransitionFrom(OrderStatus.Approved)]
    Cancelled
}
```

Apply it to the entity:
```csharp
[Entity]
[StateMachine<OrderStatus>]
public partial class Order
{
    public OrderStatus Status { get; private set; }     // yours, see below
}
```

### What the Source Generator Produces

```csharp
public partial class Order
{
    // Returns VoidResult<IError>: it never throws
    public VoidResult<IError> TransitionTo(OrderStatus targetState);

    public bool CanTransitionTo(OrderStatus targetState);

    public ReadOnlySpan<OrderStatus> AllowedTransitions();
}
```

The `Status` property is **yours**: declare it on the entity, and name it in the attribute when it is
not called `Status`.

```csharp
[Entity]
[StateMachine<OrderStatus>]
public partial class Order
{
    public OrderStatus Status { get; private set; }     // you write this
}
```

If the entity has no property of that name the generator says so (**PRAG0623**) instead of emitting
code that names a member you never wrote.

### Guarding a transition

A transition can be refused by your own code as well as by the graph. Declare a parameterless
`CanEnter{State}()` returning `bool` and the generated `TransitionTo` calls it before moving:

```csharp
public partial class Order
{
    private bool CanEnterShipped() => LineItems.Count > 0;
}
```

### Key Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[StateMachine<TEnum>]` | Entity class | Enables state machine with generated Status property |
| `[InitialState]` | Enum value | Marks the starting state (exactly one required) |
| `[TransitionFrom(name)]` | Enum value | Declares a legal source state (AllowMultiple) |
| `[RaisesEvent<TEvent>]` | Enum value | Raises domain event on successful transition (AllowMultiple) |

### One state machine per entity

`[StateMachine<TEnum>]` is not `AllowMultiple`: an entity carries one. `Property` names which property
holds the state, for when it is not called `Status`:

```csharp
[Entity]
[StateMachine<FulfillmentStatus>(Property = nameof(FulfillmentState))]
public partial class Order
{
    public FulfillmentStatus FulfillmentState { get; private set; }
}
```

A second, independent lifecycle on the same entity is usually a sign that two aggregates are hiding in
one: model it as a second entity, or drive it from your own code.

### Usage

```csharp
var order = Order.Create("ORD-001", 199.99m, customerId);
// order.Status == OrderStatus.Draft, the [InitialState]

var result = order.TransitionTo(OrderStatus.Pending);
// result.IsSuccess == true

var invalid = order.TransitionTo(OrderStatus.Delivered);
// invalid.IsSuccess == false
// invalid.Error is a Pragmatic.Result.Http.ConflictError, StatusCode 409,
// whose Reason names both states: "Cannot transition from 'Pending' to 'Delivered'."
```

`TransitionTo()` returns `VoidResult<IError>`: **it never throws**. Handle it like any other failure:

```csharp
return order.TransitionTo(OrderStatus.Approved)
    .Match(
        success: () => Results.Ok(),
        failure: e => Results.Problem(statusCode: e.StatusCode, detail: e.Message));
```
