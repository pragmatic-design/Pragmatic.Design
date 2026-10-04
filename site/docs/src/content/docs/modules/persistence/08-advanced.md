---
title: "Advanced Features"
description: "Some entities represent data that is valid only for a period of time. For example:"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/08-advanced.md
sidebar:
  order: 9
---
## Temporal Relations

### The Problem

Some entities represent data that is valid only for a period of time. For example:
- A user's role is valid from January 1st to December 31st
- An employee's salary is effective from their hire date until a raise
- A room rate applies from check-in to check-out

You need to track **when** a record is active (`ValidFrom` / `ValidTo`), enforce rules like "only one active record at a time" (a user can't have two active roles simultaneously), and detect overlapping periods.

### The Solution: `[TemporalRelation<TParent>]`

```csharp
[Entity]
[TemporalRelation<User>(MaxActive = 1, AllowOverlap = false)]
[BelongsTo<IdentityBoundary>]
public partial class UserRole
{
    public Guid UserId { get; private set; }
    public string RoleName { get; private set; } = "";
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidTo { get; private set; }    // null = currently active
}
```

### What Gets Generated

| Setting | Generated Method | What It Does |
|---------|-----------------|-------------|
| `MaxActive > 0` | `ValidateTemporalConstraints(existing)` | Checks how many records are currently active. Returns a `TemporalOverlapError` if the limit is exceeded. |
| `MaxActive == 1` | `AutoClosePrevious(existing, closedAt)` | Finds the currently active record, sets its `ValidTo` to `closedAt`, and returns the modified records so you can save them. |
| `AllowOverlap = false` | Overlap check in `ValidateTemporalConstraints` | Checks if the new record's validity period overlaps with any existing record. |

### Example: Assigning a New Role

```csharp
// Close the previous role and assign a new one
var closedRecords = UserRole.AutoClosePrevious(
    db.UserRoles.AsQueryable(),     // Existing records
    DateTimeOffset.UtcNow,          // Close timestamp
    userId);                        // Scope to this user

// closedRecords contains the previously active role(s) with ValidTo set
// Save them alongside the new role

var newRole = UserRole.Create(userId, "Admin", DateTimeOffset.UtcNow);
db.UserRoles.Add(newRole);
await db.SaveChangesAsync(ct);
```

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `MaxActive` | `int` | Maximum number of simultaneously active records. `0` = unlimited. |
| `AllowOverlap` | `bool` | Whether validity periods can overlap. Default: `false`. |

### The query extensions

Alongside the validation, the generator writes `{Type}TemporalExtensions`; this is how you read the
history the filter hides:

```csharp
public static class UserRoleTemporalExtensions
{
    public static IQueryable<UserRole> Active(this IQueryable<UserRole> query);
    public static IQueryable<UserRole> ActiveAt(this IQueryable<UserRole> query, DateTimeOffset date);
    public static IQueryable<UserRole> IncludeHistory(this IQueryable<UserRole> query);

    // one pair per temporal parent
    public static IQueryable<UserRole> ForUser(this IQueryable<UserRole> query, Guid parentId);
    public static IQueryable<UserRole> ActiveForUser(this IQueryable<UserRole> query, Guid parentId);
}
```

### Automatic Query Filter

A nested `{Type}.TemporalFilter` is generated and registered (priority 200). It keeps queries to the
currently active records (`ValidTo == null || ValidTo > Now`). Disable it through `IQueryFilterToggle`
when you need the historical rows.

---

## Inheritance

### The Problem

Some entities form a hierarchy: `ServiceFee` and `CancellationFee` are both types of `Fee`. In a relational database, you need to choose how to store this hierarchy: all in one table (TPH), separate tables joined by FK (TPT), or only concrete types (TPC). Each strategy has trade-offs.

### The Solution: `[Inheritance]`

```csharp
// Base class: abstract, no Create() generated
[Entity]
[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "FeeType")]
[BelongsTo<BillingBoundary>]
public abstract partial class Fee
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EUR";
}

// Derived classes: concrete, Create() generated for each
[Entity]
public partial class ServiceFee : Fee
{
    public string Description { get; private set; } = "";
}

[Entity]
public partial class CancellationFee : Fee
{
    public string Reason { get; private set; } = "";
    public decimal Penalty { get; private set; }
}
```

### Strategies

| Strategy | How It Works | Best For |
|----------|-------------|----------|
| `Tph` (Table-per-Hierarchy) | All types in **one table** with a discriminator column | Simple hierarchies, fast queries |
| `Tpt` (Table-per-Type) | **Separate table** per type, joined by FK | Many subtype-specific columns |
| `Tpc` (Table-per-Concrete) | Only **concrete types** have tables (no base table) | Querying each subtype independently |

### What Gets Generated

```csharp
// ═══ Generated: FeeInheritanceConfiguration.g.cs ═══
internal static class FeeInheritanceConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fee>()
            .HasDiscriminator<string>("FeeType")
            .HasValue<ServiceFee>("ServiceFee")
            .HasValue<CancellationFee>("CancellationFee");
    }
}
```

This configuration is automatically called in the generated DbContext's `OnModelCreating`.

---

## Hierarchy (Self-Referencing Trees)

### The Problem

Some entities form a tree: categories with subcategories, organizational units, comment threads. You need to query "all descendants of node X" or "all ancestors of node Y", which requires recursive queries (CTEs in SQL).

### The Solution: `[GenerateHierarchy]`

The edge of the tree is a declared self-relation; `[GenerateHierarchy]` reads it and does not declare
one of its own. The edge must be optional (`Required = false`), or the root could not exist.

```csharp
[Entity]
[Relation.ManyToOne<Category>.WithNavigation("Parent", Required = false)]   // the edge: null = root
[GenerateHierarchy]
public partial class Category : IEntity
{
    public string Name { get; private set; } = "";
}
```

`ParentId` and `Parent` are generated by the relation, never written by hand (`PRAG0619`). When the
entity has more than one self-relation (`Parent`, `MergedInto`, `Supersedes`), name the tree with
`[GenerateHierarchy(Via = "Parent")]`; without it the generator reports `PRAG0713` instead of guessing.
An entity can carry several trees, one `[GenerateHierarchy]` per edge.

### What Gets Generated

The methods are named after the navigation, so two trees on the same entity never compete for a name:

```csharp
// ═══ Generated: CategoryHierarchyExtensions, extensions on DbContext ═══
public static class CategoryHierarchyExtensions
{
    public static IQueryable<Category> GetDescendantsByParent(this DbContext dbContext, Guid rootId);
    public static IQueryable<Category> GetAncestorsByParent(this DbContext dbContext, Guid childId);
}
```

They are the entity set filtered through a recursive CTE (`FromSql` on `Set<Category>()`): the rows are
tracked entities, the query composes with `Where`/`Include`, global query filters apply, and the
starting row (the root for descendants, the child for ancestors) is not part of the result.

```csharp
var subcategories = await dbContext.GetDescendantsByParent(rootCategoryId).ToListAsync(ct);
var breadcrumb    = await dbContext.GetAncestorsByParent(leafCategoryId).Include(c => c.Parent).ToListAsync(ct);
```

**Use cases**: Category trees, organizational charts, file system hierarchies, threaded comments.

---

## Polymorphic Attachments

### The Problem

A `Document` can belong to an `Invoice`, a `Reservation`, or any other entity. With traditional inheritance (TPH/TPT), you'd need a discriminator. With foreign keys, you'd need a separate FK for each possible owner, leading to many nullable columns.

### The Solution: `[PolymorphicAttachment]` + `[Attachable<T>]`

```csharp
[Entity]
[PolymorphicAttachment]
[Attachable<Invoice>]
[Attachable<Reservation>]
[BelongsTo<BillingBoundary>]
public partial class Document
{
    public string FileName { get; private set; } = "";
    public long FileSize { get; private set; }
}
```

### What Gets Generated

```csharp
// ═══ Generated on the attachment ═══
public partial class Document
{
    public string OwnerType { get; set; } = "";     // the owner's FULL type name
    public string OwnerId { get; set; } = "";       // the owner's id, as a string
}

// ═══ Generated beside it: the read side ═══
public static class DocumentAttachmentExtensions
{
    public static IQueryable<Document> ForOwner<TOwner>(this IQueryable<Document> query) where TOwner : class;

    // one per [Attachable<T>]
    public static IQueryable<Document> ForInvoice(this IQueryable<Document> query);
    public static IQueryable<Document> ForReservation(this IQueryable<Document> query);
}
```

```csharp
var invoiceDocs = await documents.Query().ForInvoice().ToListAsync(ct);
```

⚠️ **What is generated is the read side only.** There is no `AttachTo()`: you set `OwnerType` and
`OwnerId` yourself, and nothing checks the value against the `[Attachable<T>]` list at compile time.

⚠️ **`OwnerType` must hold the type's `FullName`**, not its short name: that is what `ForOwner<T>()`
compares against. Two same-named types in different namespaces would otherwise read each other's
attachments.

---

## Lifecycle Attributes

### `[DefaultValue]`

Sets a static default value for a property when the entity is created:

```csharp
[DefaultValue(true)]
public bool IsActive { get; private set; }

[DefaultValue(1)]
public int Quantity { get; private set; }
```

### `[ComputedDefault<TEntity, TProp, TGenerator>]`

For defaults that need runtime computation, like generating sequential invoice numbers:

```csharp
[ComputedDefault<Invoice, string, InvoiceNumberGenerator>]
public string InvoiceNumber { get; private set; } = "";
```

The `InvoiceNumberGenerator` implements `IDefaultValueGenerator<Invoice, string>`:

```csharp
public class InvoiceNumberGenerator : IDefaultValueGenerator<Invoice, string>
{
    public Task<string> GenerateAsync(
        Invoice entity,
        LifecycleContext context,          // Now, UserId, TenantId, Metadata
        CancellationToken ct)
    {
        return Task.FromResult($"INV-{context.Now:yyyyMM}-{nextSeq:D5}");
    }
}
```

The generated `MutationInvoker` automatically resolves the generator from DI and calls it during entity creation.

### `[CascadeOn<TTarget>]`

Defines that when a related entity changes, this property should be updated:

```csharp
[Entity]
public partial class LineItem
{
    /// <summary>
    /// When RoomType.BaseRate changes, this price is updated automatically.
    /// </summary>
    [CascadeOn<RoomType>(nameof(RoomType.BaseRate))]
    public decimal UnitPrice { get; private set; }
}
```

The source generator produces a cascade handler that listens for changes on the source property and
propagates them to every affected row.

⚠️ **The handler finds those rows by a convention foreign key**, `{Source}Id` (`RoomTypeId` here). An
entity that declares `[CascadeOn<RoomType>]` without it is **PRAG0702**: there is nothing to filter on,
and the handler is not generated rather than being generated broken.

---

## `[GenerateTimeline]`

### The Problem

Temporal entities track validity periods, but building a timeline view (showing all historical and current records in chronological order) requires writing complex queries with ordering and gap detection.

### The Solution

```csharp
[Entity]
[TemporalRelation<Property>]
[GenerateTimeline]
public partial class RoomRate
{
    public decimal NightlyRate { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidTo { get; private set; }
}
```

### What the Source Generator Produces

The generator writes a `GetTimeline()` **on the DbContext**, and it does not return the entity: it
returns one row per validity period with the neighbouring periods attached, so the gaps and overlaps
are readable without a second pass.

```csharp
// Generated: {Namespace}.RoomRate.TimelineQuery.g.cs
public static class RoomRateTimelineExtensions
{
    public static IQueryable<RoomRateTimelineEntry> GetTimeline(this DbContext dbContext);

    public sealed record RoomRateTimelineEntry
    {
        public DateTimeOffset ValidFrom { get; init; }
        public DateTimeOffset? ValidTo { get; init; }
        public DateTimeOffset? PreviousValidTo { get; init; }   // null on the first period
        public DateTimeOffset? NextValidFrom { get; init; }     // null on the last
    }
}
```

```csharp
var timeline = await dbContext.GetTimeline().ToListAsync(ct);
var gaps = timeline.Where(e => e.ValidTo is not null && e.NextValidFrom > e.ValidTo);
```

Three things follow from how it is built (a `LAG`/`LEAD` CTE executed through `SqlQueryRaw`):

- it reads the **whole** table, partitioned by the temporal parent's FK when the entity has one; there
  is no parent-id parameter to narrow it
- it goes around the query filters, being raw SQL: history is what it is for
- it throws `InvalidOperationException` when the entity is not mapped in the `DbContext` you called it
  on, rather than returning nothing

---

## `[CascadeSource]`

### The Problem

When a property changes on one entity, related entities in other assemblies need to update their cached values. For example, when a `RoomType.Name` changes, all `LineItem` records referencing that room type should update their `RoomTypeName`.

Within the same project, `[CascadeOn<T>]` handles this automatically. But when the source and target entities are in different assemblies, the source generator can't see both sides.

### The Solution

Mark the source property with `[CascadeSource]`:

```csharp
// In Showcase.Catalog assembly
[Entity]
public partial class RoomType
{
    [CascadeSource]  // Raises EntityPropertyChanged event when changed
    public string Name { get; private set; } = "";
}
```

```csharp
// In Showcase.Billing assembly
[Entity]
public partial class LineItem
{
    [CascadeOn<RoomType>(nameof(RoomType.Name))]
    public string RoomTypeName { get; private set; } = "";
}
```

### How It Works

1. `[CascadeSource]` on `RoomType.Name` → the generated setter raises an `EntityPropertyChanged` domain event
2. The target assembly's `[CascadeOn<RoomType>]` → the SG generates an event handler that updates `LineItem.RoomTypeName`
3. Within the same assembly, `[CascadeOn<T>]` works without `[CascadeSource]`: the SG can see both sides directly

---

## `[HasPresets]` and `[PresetProvider<T>]`

### The Problem

Some entities need pre-created child records when they're created. For example, when you create a new `Hotel`, it should automatically get standard room types ("Standard", "Deluxe", "Suite") and default amenity categories. Writing this initialization logic manually is error-prone and scattered.

### The Solution

Mark the parent entity:

```csharp
[Entity]
[HasPresets]
[PresetProvider<StandardRoomTypePresets>]
[PresetProvider<DefaultAmenityPresets>]
public partial class Hotel
{
    public string Name { get; private set; } = "";
}
```

Implement the preset providers:

```csharp
public class StandardRoomTypePresets : IPresetProvider<Hotel>
{
    public Task<IReadOnlyList<object>> CreatePresetsAsync(
        Hotel parent, LifecycleContext context, CancellationToken ct)
    {
        var presets = new List<object>
        {
            RoomType.Create("Standard", parent.PersistenceId, 1),
            RoomType.Create("Deluxe", parent.PersistenceId, 2),
            RoomType.Create("Suite", parent.PersistenceId, 3),
        };
        return Task.FromResult<IReadOnlyList<object>>(presets);
    }
}
```

### How It Works

1. `[HasPresets]` tells the SG this entity has preset children
2. `[PresetProvider<T>]` names one or more providers: `AllowMultiple`, and `Order` decides the sequence
3. The **mutation invoker** calls them, in `ApplyPresetsAsync`, after the entity is constructed,
   and it is the only thing that does. The `Create()` factory is a plain static method with no
   container behind it, and a `[DomainAction]` that constructs the entity in its own `Execute` never
   passes through that step either. `[HasPresets]` on an entity nothing creates through a `[Mutation]`
   is inert, silently
4. Each provider is resolved from DI **by its concrete type**: register it (`services.AddScoped<StandardRoomTypePresets>()`)
   or the invoker throws at the first create
5. What comes back is handed to `IUnitOfWork.Add`, so the children are saved in the parent's transaction
6. Providers receive a `LifecycleContext` with current user, tenant, and timestamp

---

## `IEntityLifecycle<T>`

### The Problem

Sometimes you need to run logic when an entity is being created or saved: setting computed defaults, normalizing data, or enforcing invariants that can't be expressed with attributes alone.

### The Solution

```csharp
public class OrderLifecycle : IEntityLifecycle<Order>
{
    public void OnCreating(Order entity, LifecycleContext context)
    {
        // Set defaults before validation
        if (string.IsNullOrEmpty(entity.Currency))
            entity.SetCurrency("EUR");
    }

    public void OnSaving(Order entity, LifecycleContext context)
    {
        // Final modifications before save (after validation)
        entity.SetUpdatedAt(context.Now);
    }
}

// Register in DI
services.AddScoped<IEntityLifecycle<Order>, OrderLifecycle>();
```

### Lifecycle Hooks

| Hook | When | Use Case |
|------|------|----------|
| `OnCreating` | After construction, before validation | Set computed defaults, normalize data |
| `OnSaving` | After validation, before `SaveChanges` | Final computed fields, cross-field consistency |

Both have a default empty implementation, so implement only the one you need.

Both hooks receive a `LifecycleContext` with:
- `Now`: current UTC time (uses `TimeProvider` for testability)
- `UserId`: current user ID (if authenticated)
- `TenantId`: current tenant ID (if multi-tenant)
- `Metadata`: custom key-value data for the lifecycle scope

---

## `BatchContext`

### What it does

A scope that **defers the commit** of the mutations invoked inside it, and their domain events, so
several are persisted by one `SaveChanges`.

It names the unit of work it covers, and that is what makes it safe: a scope over Booking's unit of
work leaves Catalog's alone. The parameterless form covers every one of them, including boundaries the
opener cannot save, which is why it is a choice rather than the default shape.

⚠️ **It performs no commit itself.** Opening one also stops the invoker from claiming the commit, which
is the point: a caller saying "I will save this" must not be saved under. So you save.

```csharp
var unitOfWork = provider.GetRequiredKeyedService<IUnitOfWork>(typeof(SalesBoundary));

using (new BatchContext(unitOfWork))
{
    foreach (var row in importData)
        await _orders.CreateAsync(new CreateOrderMutation { Number = row.Number, Total = row.Total }, ct);
}

await unitOfWork.SaveChangesAsync(ct);
```

Most of the time you do not need it: an action that invokes mutations of its own boundary already
commits them once, because the outermost invoker owning a unit of work is the one that saves. Reach for
`BatchContext` when the chain has no such action at its root (a seed, a job, an import), and for
anything else use `[CommitStrategy]`.

`BatchContext` uses `AsyncLocal`: it's ambient, so any code running within its scope sees it via
`BatchContext.Current`. Nested contexts are supported (each restores the previous on dispose), and a
mutation entering an already-open batch reuses it rather than opening a second one.

### Deferred events

```csharp
IReadOnlyDictionary<Type, IReadOnlyList<object>> events = batch.GetEventsByType();
```

Grouped by runtime type, non-generic: there is no `GetEventsByType<T>()`.

### Not implemented

`BulkOperationOptions.ChunkSize` and `ChunkAsTransaction` are **reserved and not honored**.
Nothing reads them: the whole batch goes into the caller's single `SaveChanges` whatever they are set
to. Setting `ChunkSize = 500` does not chunk.
