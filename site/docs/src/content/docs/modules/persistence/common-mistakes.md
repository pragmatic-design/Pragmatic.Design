---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Persistence. Each section shows the wrong approach, the correct approach, and explain"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/common-mistakes.md
sidebar:
  order: 31
---
These are the most common issues developers encounter when using Pragmatic.Persistence. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Entity Class

**Wrong:**

```csharp
[Entity]
[BelongsTo<SalesBoundary>]
public class Order
{
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

**Compile result:** `PRAG0600` error -- "Entity type 'Order' must be declared as partial".

**Right:**

```csharp
[Entity]
[BelongsTo<SalesBoundary>]
public partial class Order
{
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

**Why:** The source generator emits `PersistenceId`, `Id`, `Create()`, `SetXxx()` and the nested `Repository` class into separate `.g.cs` files. Without `partial`, the compiler cannot merge your declaration with the generated one. This is the most common onboarding mistake.

---

## 2. Entity Without `[BelongsTo<TBoundary>]`

**Wrong:**

```csharp
[Entity]
public partial class Invoice
{
    public decimal Total { get; private set; }
}
```

**Compile result:** The SG generates basic entity members (`PersistenceId`, `Id`, `Create()`, setters) but does **not** generate: the repository, the EF Core entity configuration, the DbContext DbSet, or the DI registration. At runtime, `IRepository<Invoice>` cannot be resolved from DI.

**Right:**

```csharp
[Entity]
[BelongsTo<BillingBoundary>]
public partial class Invoice
{
    public decimal Total { get; private set; }
}
```

**Why:** Boundaries are the organizing unit for persistence. Without `[BelongsTo]`, the SG does not know which DbContext should include the entity. Repository generation, entity configuration, and DI registration all depend on the boundary assignment. If you see "No service for type IRepository<Invoice> has been registered", the first thing to check is the `[BelongsTo]` attribute.

---

## 3. Manual Navigation Properties Instead of `[Relation.*]`

**Wrong:**

```csharp
[Entity]
[BelongsTo<SalesBoundary>]
public partial class Order
{
    public Guid CustomerId { get; private set; }
    public Customer? Customer { get; set; }              // Manual navigation
    public ICollection<LineItem> Items { get; } = [];    // Manual collection
}
```

**Compile result:** `PRAG0619` on `CustomerId`, `Customer` and `Items`. It is an error because compiling it would be the problem: a hand-written pair would be inferred from its shape with every option at its default, and a hand-written key alone would reach nothing, so the generated configuration and the members you wrote would describe two different models and nothing would tell you.

**Right:**

```csharp
[Entity]
[BelongsTo<SalesBoundary>]
[Relation.ManyToOne<Customer>]
[Relation.OneToMany<LineItem>]
public partial class Order
{
    public string OrderNumber { get; private set; } = "";
}
```

**Why:** Pragmatic requires `[Relation.*]` attributes as the single source of truth for relationships. The SG generates FK properties, navigation properties, and EF Core configuration from these attributes. A manual member is a second model the generator reads at most halfway. Other generators do not need it in source: `[MapFrom]`, `[GenerateHierarchy]`, `[TemporalRelation]`, `[CascadeOn]`, `[Lookup]` and raised events predict the generated key from the declared relation. A specific key name is `.WithNavigation("Customer", ForeignKey = "BuyerId")`.

---

## 4. Declaring no database, or not placing the module on one

**Wrong:**

```csharp
// No database declaration in the host project
// Entities have [BelongsTo<BillingBoundary>] but nothing configures the DbContext
```

**Runtime result:** The SG generates repositories and entity configurations, but no DbContext is configured. At startup, `InvalidOperationException` because the DbContext is not registered in DI.

**Right:** declare a database in the host, then say which module lives on it.

```csharp
// In the host project
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;

[Include<BookingModule, AppDatabase>]
[Include<BillingModule, FinancialDatabase>]     // a second database when they must not share one
public sealed class AppHostModule;
```

**Why:** the SG generates entity configurations and repositories in the module assembly, but the
topology is a host decision — which modules share a connection is not something a module can know. The
`DbContext` types are generated from that pairing, one per boundary, and registered by the generated
`AddAllPragmaticDbContexts()`. You never write a `DbContext` class, and there is nothing to mark
`partial`.

---

## 5. Using `new Entity()` Instead of `Create()`

**Wrong:**

```csharp
var order = new Order();
order.SetOrderNumber("ORD-001");
order.SetTotal(150m);
orders.Add(order);
```

**Compile result:** **PRAG0680**, a warning with a code fix that rewrites it for you.

**Right:**

```csharp
var order = Order.Create("ORD-001", 150m);
orders.Add(order);
```

**Why — and what is *not* the reason:** the id is fine either way on a `Guid` key. The generated trait
initialises `PersistenceId = Guid.CreateVersion7()` in the property initialiser, so `new Order()` gets a
valid v7 id too. What `new` skips is everything else the factory states: the **required** properties
become the caller's job to remember, and nothing marks the entity as new. On an `int` or `string` key
neither form assigns anything — the database does, or you do.

---

## 6. Forgetting to Register Generated Query Filters

**Wrong:**

This one is now **only possible outside a Pragmatic host** — the generated host calls every module's
filter registration itself. Wiring persistence by hand, in a test project or a console app, it is
still on you:

```csharp
// Wrong — the filters are generated and never registered
services.AddBillingDbContext(o => o.UseNpgsql(connection));
services.AddPragmaticPersistenceRepositories<BillingDbContext>();

// Right
services.AddBillingDbContext(o => o.UseNpgsql(connection));
services.AddPragmaticPersistenceRepositories<BillingDbContext>();
services.AddMyAppQueryFilters();
```

**Runtime result of the wrong one:** repositories resolve, queries execute, and every read is
**unfiltered** — soft-deleted rows come back, tenants see each other. Nothing throws, which is what
makes it worth checking for.

**Why:** the filter classes (`Order.SoftDeleteFilter` and friends) are generated but not registered by
themselves. `IQueryFilterProvider.GetCombinedFilter<T>()` returns nothing when nothing is registered,
and a `Where` that was never added cannot fail. The method is `Add{Prefix}QueryFilters()`, where
`{Prefix}` comes from the entities' common namespace — `AddGeneratedQueryFilters()` when they share
none.

There are two more that stay yours to call even **inside** a host, because the host does not call
them: `Add{Prefix}LookupCaches()` for `[Lookup]` and `Add{Prefix}CascadeHandlers()` for `[CascadeOn]`.

---

## 7. Querying Across Boundaries with Include()

**Wrong:**

```csharp
// Invoice is in BillingBoundary, Reservation is in BookingBoundary
var invoice = await invoiceRepo.GetByIdAsync(
    invoiceId,
    q => q.Include(i => i.Reservation),  // Cross-boundary Include!
    ct);
```

**Runtime result:** Compile error or runtime `InvalidOperationException` -- `Reservation` is not part of the Billing DbContext. Navigation property `Invoice.Reservation` does not exist because cross-boundary navigations are not generated.

**Right:**

```csharp
var invoice = await invoiceRepo.GetByIdAsync(invoiceId, ct);
var reservation = await reservationRepo.GetByIdAsync(invoice.ReservationId, ct);
```

**Why:** Each boundary maps to a separate DbContext. Entities in different boundaries live in different database contexts, so EF Core `Include()` cannot traverse between them. The SG generates only the FK property (`ReservationId`) for cross-boundary relations -- not the navigation property. Load cross-boundary data with separate repository calls.

---

## 8. Using typeof() Instead of Generic Attributes

**Wrong:**

```csharp
[BelongsTo(typeof(BillingBoundary))]
[Relation.ManyToOne(typeof(Customer))]
public partial class Invoice { /* ... */ }
```

**Compile result:** it does not compile. There is no non-generic `BelongsToAttribute` and no
non-generic `Relation.ManyToOne` — the generic form is the only one there is.

**Right:**

```csharp
[BelongsTo<BillingBoundary>]
[Relation.ManyToOne<Customer>]
public partial class Invoice { /* ... */ }
```

**Why:** Pragmatic attributes are always generic. `[BelongsTo<T>]`, `[Entity]`, `[Relation.ManyToOne<T>]`, `[StateMachine<TEnum>]` -- all use type parameters. The generic form provides compile-time type checking (the referenced type must exist and be accessible) and produces cleaner code without `typeof()` noise.

---

## 9. Expecting IRepository to Expose Logic Key Helpers

**Wrong:**

```csharp
public class OrderService(IRepository<Order> orders)
{
    public Task<Order?> GetByNumber(string number, CancellationToken ct)
        => orders.GetByOrderNumberAsync(number, ct);  // Does not compile!
}
```

**Compile result:** `IRepository<Order>` does not have `GetByOrderNumberAsync`. This method exists only on the generated concrete repository `Order.Repository`.

**Right:**

```csharp
public class OrderService(Order.Repository orders)
{
    public Task<Order?> GetByNumber(string number, CancellationToken ct)
        => orders.GetByOrderNumberAsync(number, ct);  // Works
}
```

Or, if you want to keep the stable interface for other operations:

```csharp
public class OrderService(
    IRepository<Order> orders,       // Stable CRUD
    Order.Repository orderRepo)            // Concrete for LogicKey
{
    // Use orders for Add/Remove/Find
    // Use orderRepo for GetByOrderNumberAsync
}
```

**Why:** The stable `IRepository<T>` interface intentionally stays small -- `GetByIdAsync`, `FindAsync`, `CountAsync`, `ExistsAsync`, `Add`, `Remove`, `Update`. Logic key helpers, include overloads, and bulk methods are on the concrete generated repository because they are entity-specific. This separation keeps the interface stable across all entities while allowing per-entity convenience methods on the concrete type.

---

## 10. Nullable Properties on Create Mutations

**Wrong:**

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateOrderMutation : Mutation<Order>
{
    public string? OrderNumber { get; init; }   // Nullable on create!
    public decimal? Total { get; init; }         // Nullable on create!
}
```

**Runtime result:** Both properties can be `null`. The generated `ApplyToEntity` will skip them (null = "don't change"), so the entity gets created with default values for `OrderNumber` and `Total` -- probably empty string and zero. No validation error is raised.

**Right:**

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateOrderMutation : Mutation<Order>
{
    public required string OrderNumber { get; init; }   // Required on create
    public required decimal Total { get; init; }         // Required on create
}
```

**Why:** in `MutationMode.Create` mark `required` whatever the entity cannot be without. The keyword
travels to the generated body DTO — `public required string OrderNumber { get; init; }` on a
`partial record` — so a request that omits the field fails deserialization, and the endpoint answers
**400 naming `body`** instead of writing an entity with an empty string in it. In `MutationMode.Update`
the properties are nullable, because there `null` means "leave it alone".

---

## 11. Throwing Exceptions Instead of Returning Errors

**Wrong:**

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class ConfirmOrderMutation : Mutation<Order, ConflictError>
{
    public required Guid Id { get; init; }

    public override async Task<Result<Order, IError>> ApplyAsync(
        Order entity, CancellationToken ct)
    {
        if (entity.Status != OrderStatus.Pending)
            throw new InvalidOperationException("Order must be pending");

        entity.TransitionTo(OrderStatus.Approved);
        return entity;
    }
}
```

**Runtime result:** The exception propagates as a 500 Internal Server Error. The `ConflictError` in the class signature is never used. The OpenAPI spec declares a 409 response that never happens.

**Right:**

```csharp
[Mutation(Mode = MutationMode.Update)]
[TransitionsTo<OrderStatus>(OrderStatus.Approved)]
public partial class ConfirmOrderMutation : Mutation<Order>
{
    public required Guid Id { get; init; }
}
```

The move is the state machine's to refuse: the generated invoker performs it and answers 409 — the
`ConflictError` naming both states — when the order is not pending, and the endpoint documents that 409.
A refusal of your own belongs in the body as a returned error, never a throw
([State Machine](/modules/persistence/19-state-machine/#usage-in-an-operation-transitionsto) for when the invoker moves the
entity relative to it).

**Why:** The mutation pipeline uses the Result pattern. The generated endpoint handler maps each error type to an HTTP status code (`NotFoundError` = 404, `ConflictError` = 409, `ValidationError` = 422). Throwing an exception bypasses the pipeline, loses type information, and always produces 500. Reserve exceptions for truly unexpected failures (database connection lost), not for expected business conditions.

---

## 12. Disabling Query Filters Too Broadly

**Wrong:**

```csharp
public class OrderService(IQueryFilterToggle filterToggle)
{
    public async Task<List<Order>> GetRecentOrders(CancellationToken ct)
    {
        using (filterToggle.UseMode(FilterMode.Raw))
        {
            // Raw mode bypasses ALL filters: soft-delete, tenant, permission
            return await orders.FindAsync(
                Spec<Order>.Where(o => o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-7)), ct);
        }
    }
}
```

**Runtime result:** Works, but returns soft-deleted orders, orders from other tenants, and orders the current user should not see. In a multi-tenant system, this is a data leak.

**Right:**

```csharp
public class OrderService(IQueryFilterToggle filterToggle)
{
    public async Task<List<Order>> GetRecentOrdersIncludingDeleted(CancellationToken ct)
    {
        // Disable only the specific filter you need to bypass
        using (filterToggle.Disable<Order.SoftDeleteFilter>())
        {
            return await orders.FindAsync(
                Spec<Order>.Where(o => o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-7)), ct);
        }
    }
}
```

Or if you need an admin view:

```csharp
using (filterToggle.UseMode(FilterMode.Admin))
{
    // Skips visibility and permission filters, keeps soft-delete and tenant
}
```

**Why:** the modes are ordered and cumulative — `Normal`, `Admin`, `Elevated`, `Background`, `Raw` —
and each skips everything the one before it skips. Use the narrowest scope that satisfies your requirement. `FilterMode.Raw` is for migrations, data repair scripts, and support tooling -- not for regular business logic. If a screen always needs raw data, that is often a modeling smell.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Missing `partial` | `PRAG0600` compile error |
| Missing `[BelongsTo]` | No repository/DbContext generated, DI resolution failure |
| Manual navigation property | Silent: two conflicting models, resolved by EF Core inference |
| No `[PragmaticDatabase]`, or no `[Include<TModule, TDatabase>]` | DbContext not registered, `InvalidOperationException` at startup |
| `new Entity()` instead of `Create()` | `PRAG0680` warning with a code fix — the id is fine, the required properties are not |
| Missing `AddMyAppQueryFilters()` | Deleted/tenant rows visible, no filter enforcement |
| Cross-boundary `Include()` | Compile error or `InvalidOperationException` |
| `typeof()` in attribute | Compile error or lost type safety |
| IRepository for logic key helpers | Compile error, method not on interface |
| Nullable props on Create mutation | Silent default values, missing required data |
| Throwing exceptions in ApplyAsync | 500 instead of typed HTTP status |
| `FilterMode.Raw` in business logic | Data leak, deleted/tenant rows visible |
