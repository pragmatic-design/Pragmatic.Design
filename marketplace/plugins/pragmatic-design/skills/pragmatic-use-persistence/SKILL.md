---
name: pragmatic-use-persistence
description: Use when modeling entities, repositories, queries and filters, projections, patches, identifiers or the DbContext — Pragmatic.Persistence and its EF Core provider, attribute-first. Schema changes are pragmatic-use-migrations.
---

# Pragmatic Use Persistence

**Covers:** Model entities, repositories, queries, filters, projections, patches, identifiers and DbContext with Pragmatic.Persistence + Pragmatic.Persistence.EFCore consumed from NuGet. Attribute-first pattern driven by the source generator.

Pragmatic.Persistence is an **attribute-driven, code-generation** framework. You decorate classes and properties; the unified source generator (`Pragmatic.SourceGenerator`) produces the full entity, factory, repository, query, EF configuration, and DbContext. The consumer calls the generated code.

This skill covers: what to decorate, what the SG produces, and how to consume it.

## When to use

- You are modeling entities/aggregates, repositories, queries, projections.
- You are configuring one or more databases (PostgreSQL/SQLServer/SQLite).
- You are adding cross-cutting persistence logic: soft-delete, audit, ownership, scoped data, state machine, patch, hierarchies, value objects.

For HTTP APIs exposed from these mutations/queries: see `pragmatic-use-actions-endpoints`. For DI/host: `pragmatic-use-composition`.

## Packages

```xml
<PackageReference Include="Pragmatic.Persistence" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Persistence.EFCore" Version="1.0.0-alpha.1" />        <!-- EF runtime -->
<PackageReference Include="Pragmatic.Migrations" Version="1.0.0-alpha.1" />                <!-- schema diff -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Identifiers (`Guid7`, `OpaqueId`, `ShortGuid`, `Slug`) are in `Pragmatic.Persistence.Identifiers` (same package `Pragmatic.Persistence`).

## API surface

Dense reference with all signatures: **`../pragmatic-ecosystem/references/api-surface/persistence.md`**.
Full diagnostics: **`../pragmatic-ecosystem/references/diagnostics.md`** (ranges PRAG0600-0699 + PRAG1100-1109).
Complete E2E setup (csproj, Program.cs, entity, mutation, query, endpoint): **`../pragmatic-ecosystem/references/cookbook/crud-web-api.md`**.
Modelling the domain itself — namespaces, `IEntity`, relations, cross-boundary contracts, and the
members the generator already writes: **`../pragmatic-ecosystem/references/cookbook/domain-model.md`**.

## Mental model

```
                      Source generator
   You decorate ─────────────▶  generates ─────────────▶  You consume
  [Entity]      Entity.Create.g.cs     Order.Create(...)
  [Auditable]      Entity.Traits.g.cs     order.CreatedAt
  [SoftDelete]     SoftDeleteFilter       repo.Query()
  [LogicKey]       Repo.GetByXAsync       repo.GetByCodeAsync("X", ct)
  [Query<E,DTO>]   Query.Apply().Project  runner.RunAsync(query)
  [PragmaticDatabase] DbContext.g.cs      sp.GetRequiredService<IRepository<T>>()
```

The SG runs two passes:
1. **Library-side**: for each boundary library, generates `Traits`, `Create`, `Setters`, `Specs`, `Relations`, the repository and `Query.Apply`.
2. **Host-side**: for the Host project, aggregates the referenced boundaries, generates `EntityConfiguration`, `DbContext`, `IRepository<T>` impl, and registers DI.

## Core patterns

### 1. Boundary + Module

A domain library is a **boundary**. Mark it with two empty classes:

```csharp
[Boundary] public partial class SalesBoundary;          // Pragmatic.Actions.Attributes
[Module(Name = "MyApp.Sales")] public sealed class SalesModule;   // Pragmatic.Composition.Attributes
```

All code under `MyApp.Sales.*` belongs to the `Sales` boundary. Sub-folders (`Orders/`, `Customers/`) create sub-boundaries inferred from the namespace — the SG uses these to generate separate action interfaces.

### 2. Entity

```csharp
[Entity]
[Auditable]
[SoftDelete(Cascade = true)]
public partial class Order
{
    [LogicKey]
    [GeneratedValue("ORD-{YYYY}{MM}-{SEQ:5}")]
    public string OrderNumber { get; private set; } = default!;

    public Guid CustomerId { get; private set; }
    public decimal Total { get; private set; }

    [Projectable]                                // SQL-translatable expression
    public bool IsHighValue => Total >= 1000m;
}
```

The SG generates:
- `Order.Create(orderNumber, customerId, total)` — factory; `new Order()` triggers **PRAG0680**.
- `Order.PersistenceId` — a `Guid` key is assigned at construction with `Guid.CreateVersion7()` — and
  `Order.Id`, a read-only alias for it. **Equality is not generated**: an entity is a class and compares
  by reference. Compare `PersistenceId`, or write the members yourself.
- **`SetOrderNumber(...)`, `SetTotal(...)` — one setter per `private set` property**, in `*.Setters.g.cs`. Writing them by hand duplicates a member the generator owns and the build fails with **CS0111**; add a domain method only when it does more than assign.
- Audit + SoftDelete properties populated by interceptors on `SaveChanges`.
- `Order.Expr.IsHighValue` — `Expression<Func<Order,bool>>` usable in `.Select`/`.Where`.

⚠️ **Two `[LogicKey]` properties make one composite key**, and their order decides the index's leading
column — the only one it can be searched by alone — and the parameters of
`GetByCodeAndSeasonAsync(code, season)`. Say it with `[LogicKey(Order = n)]`, lower first; unset keeps
declaration order, so nothing existing moves. Unset means `0`, so setting it on one part of two puts
the **other** first. Without it, moving a property up or down in the class is a schema change *and* a
signature change, and the build stays green.

### 3. Database

Each logical database is an empty decorated class, declared in the **Host project**:

```csharp
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

⚠ The database marker alone is **not** enough. The Host must also declare a `[Module]` topology class
that maps each module to a database via `[Include<TModule, TDatabase>]` — otherwise the host reports
*"No databases configured"* and the DbContext / `IUnitOfWork` are never registered (startup DI failure):

```csharp
[Module]
[Include<SalesModule, AppDatabase>]          // one per module → database
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class HostModule;
```

Also add the EF provider matching Pragmatic's EF Core major (`.NET 10 → EF Core 10`):
`Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.*` (`Pragmatic.Persistence.EFCore` bundles none).
The SG then generates the DbContext and registers it. Multiple databases? Declare multiple
`PragmaticDatabase` markers and route modules to each via `[Include<,>]`.

> Entities must live in the `{Boundary}.Entities` namespace — the SG derives the host database root
> namespace from it; a feature-folder namespace breaks the generated migration/schema types.

There is no hand-written DbContext to fall back on. To add to the model of the generated one, implement its partial hook in a `partial` part of the context, in the host (the generated `{Boundary}DbContext` lives in `{Boundary}.Entities`):

```csharp
namespace Sales.Entities;

public partial class SalesDbContext
{
    // Runs after every generated configuration, so it can add to or override it.
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Order>().Property(o => o.Notes).HasConversion(new NotesConverter());
}
```

⚠️ The hook changes the model **EF holds in memory**, not the database: `Pragmatic.Migrations` builds the schema from the generated schema, which reads the declarations, not this method. A value conversion or a query-side setting belongs here; an index, a column or a table added here is not migrated — a unique index is declared with `[Unique]`, a table with an `[Entity]`. The migration context (`{Database}MigrationDbContext`) has its own hook and is not affected by this one.

### 4. Migrations

`Pragmatic.Migrations` replaces EF Migrations:

```csharp
await PragmaticApp.RunAsync(args, app => app.UsePragmaticMigrations());
```

On an empty database it creates the schema from decorated entities; on an existing database it
applies a declarative diff, inside one transaction, at startup. No `Add-Migration`, no migration
files. Data-losing changes (DROP, SET NOT NULL, narrowing types) are blocked unless you opt in with
`Force()`, and a failed migration aborts host startup.

For breaking changes, data backfills (`IDataMigration`), the committed schema snapshot, DB-per-tenant
and the `pragmatic-migrate` CLI: skill **`pragmatic-use-migrations`**.

### 5. Repository injection

Generated automatically. Inject it into mutations/handlers:

```csharp
public sealed class CompleteOrder(
    Order.Repository orders,                                          // concrete: GetByOrderNumberAsync
    [FromKeyedServices(typeof(SalesBoundary))] IUnitOfWork uow)       // keyed — unkeyed does not resolve
{
    public async Task<Result<Order, NotFoundError>> Handle(string code, CancellationToken ct)
    {
        var order = await orders.GetByOrderNumberAsync(code, ct);   // from [LogicKey] — concrete repo only
        if (order is null) return NotFoundError.Create("Order", code);   // Pragmatic.Result.Http
        order.MarkPaid();
        await uow.SaveChangesAsync(ct);
        return order;
    }
}
```

⚠️ Two resolution details the compiler will not catch. `GetByOrderNumberAsync` is on the **concrete**
`Order.Repository`, so injecting `IRepository<Order>` does not compile against it. And
`IUnitOfWork` is registered **keyed by the boundary marker** — inject it unkeyed and the container
throws at resolution, not at build.

`IRepository<T>` exposes `Add/AddRange/Update/Remove/RemoveRange` plus the read side
`GetByIdAsync/FindAsync/CountAsync/ExistsAsync/FirstOrDefaultAsync/Query()/RunAsync(query)`.

⚠️ **A rule with a name goes in a `Specification<T>` property**, not restated as filters: the generator
applies any such property whole, in `Apply` and in `ToSpecification`, and `[BindSpecification]` marks the
input it reads (**PRAG0709** if there is no specification to feed). ⚠️ `[Query]` on a class that is not
`partial` generates nothing — **PRAG0712**.

⚠️ **A query property becomes a filter when it carries `[Filter]`, or is `required`, or is
`[FromCurrentUser]`, or is nullable.** A property that is none of these would be read and dropped —
`Apply` untouched, `ToSpecification` returning `Spec.True`, so a `Single = true` query would answer 200
with whichever row came first. That is **`PRAG0707`**, an error. ⚠️ **`[GenerateHierarchy]`** has the
same shape: the parent is found by name (`ParentId` or `{Type}ParentId`), and anything else is
**`PRAG0708`**.

A filter or sort on the entity's generated `Id` targets `PersistenceId` on its own (the alias is
unmapped, and a `Where` on it would not translate), exactly as an update mutation's `Id` does — so a
get-by-id is `public required Guid Id { get; init; }` and nothing else. An explicit `MapTo` still wins,
and an `Id` you declared yourself as a column stays a column.

### Where each generated helper hangs

The generator writes helpers in three places, and knowing which is which is the difference between
finding them and concluding they do not exist.

| Generated | Hangs off | Reach it with |
|---|---|---|
| `GetByIdAsync(id, includes)`, `GetBy{LogicKey}Async`, the bulk methods, `SaveChangesAsync` | the **concrete** `{Entity}.Repository` | inject the nested type, not `IRepository<T>` |
| `{Entity}Specifications.ById(...)` and one per key | a static class | `repo.FindAsync(OrderSpecifications.ByCode(code), ct)` |
| `ById(this IQueryable<T>, …)`, `Active()`, `ActiveAt(date)`, `WithIncludesFor{Dto}()` | `IQueryable<T>` | `repo.Query().Active().ActiveAt(when)` |
| `IncludeHistory(IQueryFilterToggle)` — a scope that lifts the temporal filter | the **entity type**, static | `using (UserRole.IncludeHistory(filters)) { … repo.Query() … }` |

⚠️ **They are not on the repository, and that is deliberate.** `Query()` returns the `IQueryable<T>` the
extensions compose on, so one member carries all of them — and the same extension works on a filtered
set, a projection, or a queryable you assembled yourself. Mirroring each one onto the repository would
double a surface that already has 25 members and give one operation two spellings, which is how the two
drift.

The cost is discoverability: typing `repo.` does not show them. The composition to remember is
**`repo.Query()` first, then the extension**:

```csharp
var current = await _roles.Query()
    .ForUser(userId)          // generated by [TemporalRelation<User>]
    .Active()                 // generated: hides history
    .ToListAsync(ct);

var byCode = await _orders.FindAsync(OrderSpecifications.ByCode("ORD-1"), ct);
```

⚠️ **`GetBy{LogicKey}Async` is NOT on the interface** — it, the `includes` overload of `GetByIdAsync`,
the bulk methods and `SaveChangesAsync` live on the concrete nested `{Entity}.Repository`. Inject that
type when you need them. Commit is via `IUnitOfWork` (the generated invoker does it for
mutations/actions), which also carries `Detach`, `State`, `SavepointAsync` and
`RollbackToSavepointAsync`.

### 6. Declarative queries

**Four forms, shortest first.** They are not a matter of taste: each is the shortest thing that
holds its case, and the next one exists for what the previous cannot express. Go up a rung only when
the one below does not reach — and a surviving `Query()` says in a comment why.

| Form | Use it when |
|---|---|
| `[Query]` on a **specification** | the rule is already written as a predicate; publish it |
| `[Query]` on a **class** | the read composes: several filters, a sort, a page, a projection |
| `repository.RunAsync(query, ct)` | reuse **that same** declared query inside an operation that already holds the repository |
| `repository.Query()` | what none of them covers — compose the `IQueryable` by hand |

**Rung 3 — run a declared query where you already are.**

```csharp
var page = await _properties.RunAsync(
    new SearchDeactivatedPropertiesQuery { City = city, PageSize = 100 }, ct);

foreach (var summary in page.Items.Where(p => !p.IsActive))
    …
```

The unpaged sibling answers `IReadOnlyList<TResult>`. The `DbContext` never appears in your code,
which is the point: a declared read is reused as it is, not written again in LINQ.

⚠️ **No operation pipeline runs here**, deliberately: you are already inside a `[DomainAction]` that
has its own validation, permission and transaction. The path *with* the pipeline is the boundary
facade (`catalog.CatalogItems.SearchCatalogItems(...)`), which invokes the query as an operation.
Choose `RunAsync` to reuse a read; choose the facade to invoke an operation.

⚠️ **A filter the query lifts is lifted for the read and not for the rest of your operation.** A
`[WithoutFilter<T>]` on the query applies to the rows `RunAsync` answers with; loading one of those
rows afterwards with `GetByIdAsync` goes through the repository's ordinary filters and may not find
it. Declare the same `[WithoutFilter<T>]` on the operation when it has to act on what it read.

**Rung 1 — publish a rule you already wrote.** `[Query]` sits on a **static** member returning
`Specification<TEntity>`, and derives `{Member}Query` beside it. The member's parameters become the
query's inputs; the specification itself is not touched, so it still composes with `&`:

```csharp
public static partial class OrderSpecifications      // the other half of the entity's generated partial
{
    [Query<Order>(Paged = true)]
    [Endpoint(HttpVerb.Get, "api/orders/pending")]     // opt-in: no [Endpoint], no route
    [RequirePermission(SalesPermissions.Order.Read)]
    public static Specification<Order> Pending()
        => Spec<Order>.Where(o => o.Status == OrderStatus.Pending);
}
```

⚠️ The derived name is `{Member}Query` **in the container's namespace**, so two specifications with
the same member name in one namespace would derive one type twice — **PRAG0726**, an error. Rename
one, or move it to a namespace of its own.

**Rung 2 — the class, when the read composes.**

```csharp
[Query<Order, OrderSummary>(Paged = true)]
public partial class SearchOrdersQuery
{
    public Guid? CustomerId { get; init; }                              // null = skip
    [Filter(Operator = FilterOperator.Contains)]
    public string? OrderNumber { get; init; }
    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Total")]
    public decimal? MinTotal { get; init; }
    [Sort(MapTo = "CreatedAt", DefaultDirection = SortDirection.Descending)]
    public SortDirection? CreatedAtSort { get; init; }
}
```

**`Paged = true` writes the paging surface** — `Page = 1`, `PageSize = 20`. Write them by hand only
to change the defaults; declaring both is **PRAG0727**, because the generated half cannot add a member
the author already wrote.

Generates `Apply(IQueryable<Order>)` with conditional filters, sort, paging, and `Projection: Expression<Func<Order,OrderSummary>>` (auto-generated if fields match; otherwise add `[MapFrom<Order>]` on `OrderSummary` — see `pragmatic-use-foundation`).

`[Filter(IgnoreCase = true)]` compares without case, on string properties only.

**A declared default is honoured.** `public State? Status { get; init; } = State.Pending;` means the
listing shows pending rows when the caller says nothing, and `PageSize = 50` means fifty. The generated
endpoint keeps the initializer where the query string omits the parameter, and paging falls back to the
framework's `1`/`20` only for a query that declares neither.

⚠️ Consequence worth stating: with a default, a caller can no longer ask for "no filter" by omitting the
parameter — that is what declaring one means. Leave the property without an initializer if omitting it
should mean *everything*.

**Reading exactly one row — `Single = true`.** A get-by-id is a query with one filter and no paging,
so say that instead of hand-writing an endpoint with a repository and a `NotFoundError`:

```csharp
[Query<Order, OrderDetail>(Single = true)]
[Endpoint(HttpVerb.Get, "api/orders/by-id/{id}")]
public partial class GetOrderQuery
{
    public required Guid Id { get; init; }
}
```

The endpoint returns the DTO itself rather than a list, and answers **404** when nothing matches —
the executor takes the first row (`FirstOrDefault`, not `Single`), so a filter that is not unique is
not an error. Without `Single` the same query returns a list, and an empty result is a 200 with `[]`.

`Id` needs no `MapTo`: a filter on the entity's generated `Id` targets `PersistenceId`, the column
behind it, in single and list queries alike (`List<Guid>? Id` filters the key with `In`).

**Reading by the caller — `[FromCurrentUser]`.** "My balances", "my profile": the filter is who is
asking, so it must not be an input the caller sends. Mark the property and let the query's invoker
fill it:

```csharp
[Query<Allowance, AllowanceBalanceDto>]
[RequirePermission(ManageOwnProfile.Value)]
[Endpoint(HttpVerb.Get, "api/me/balances")]
public partial class GetMyBalancesQuery
{
    [FromCurrentUser(nameof(Employee.Id))]      // using Pragmatic.Identity
    public Guid EmployeeId { get; private set; }

    [Filter] public required int Year { get; init; }
}
```

- `[FromCurrentUser]` binds `ICurrentUser.Id` to a `string`; `[FromCurrentUser(nameof(Employee.Id))]`
  binds that member of the `[PragmaticUser]` entity (same compilation, `Pragmatic.Identity.Persistence`
  referenced), read through its generated `{User}Resolver`, which the invoker constructs itself.
- The invoker writes it **after validation and the permission check, before the read**: not signed in
  → 401, no user entity → 404.
- `{ get; private set; }` — **PRAG0730** otherwise. It is never a query-string, route or body
  parameter, never in OpenAPI, never an argument of the boundary member; it **is** in the `[Cacheable]`
  key. A bound property is a filter, always applied and matched with `==` — no `[Filter]` needed.
- **PRAG0731** when it cannot be generated: unknown member, different type, `nameof` over another type,
  no `[PragmaticUser]`/resolver, member-less form on a non-`string`.
- ⚠️ Do not inject `ICurrentUser` into a query instead: the answer must be a function of the
  properties, or the cache key, the serialized query and the contract cannot see what changed it.

**Reading by the clock — `[FromClock]`, with a `[ComputedFilter]` method.** "Who is away today": the
date must come from the application's clock, not from the caller and not from the database. Put the
rule on the entity as a method that takes the day, and let the invoker fill the day:

```csharp
// On the entity
[ComputedFilter]
public bool IsAwayOn(DateOnly day)
    => LeaveRequests.Any(LeaveRequestSpecifications.Approved & LeaveRequestSpecifications.Covering(day));

// On the query
[FromClock]                                   // using Pragmatic.Temporal.Clock
public DateOnly Today { get; private set; }  // IClock.UtcToday; a DateTimeOffset gets UtcNow

[BindSpecification] public bool? AwayToday { get; init; }

public Specification<Employee>? WhoIsAwayToday => AwayToday switch
{
    true => EmployeeComputedFilters.IsAwayOnSpec(Today),
    false => !EmployeeComputedFilters.IsAwayOnSpec(Today),
    null => null
};
```

- A `[ComputedFilter]` method generates `{Name}Spec(params)` and `Where{Name}(params)`. It is
  translated to SQL like a property; a navigation the relation generates is read from the row.
- A rule that is already a specification is **named, not repeated**, in a `[ComputedFilter]` or
  `[Projectable]` body: `LeaveRequests.Any(LeaveRequestSpecifications.Covering(day))`,
  `Requests.Where(LeaveRequestSpecifications.Approved).Sum(r => r.Amount)`. The query receives the
  specification's expression (composed, parameterized). ⚠️ Its arguments cannot come from the row — a
  column, a lambda parameter of the body: **PRAG0735**.
- ⚠️ Not `DateTime.UtcNow` in a `[ComputedFilter]` property: the database evaluates it with its own clock,
  and a clock the host or a test injects never reaches it.
- `[FromClock]` is no filter and no parameter (query string, route, OpenAPI, boundary); it is in the
  `[Cacheable]` key. The invoker needs an `IClock` registered (Temporal does it).
- Both attributes work on a `[DomainAction]` and a `[Mutation]` too, with the same rules — the decision
  stamped with `Now`, the withdrawal refused from `Today` — instead of an injected `IClock` or
  `ICurrentUser`: see `pragmatic-use-actions-endpoints` → *What does not go in `Execute`*.
- **PRAG0733**: a `[ComputedFilter]` the generator cannot write. **PRAG0734**: a `[FromClock]` on a type
  the clock does not give, or with a setter anyone else reaches.

**Counting instead of listing — a query whose result is a view.** A `GROUP BY` answers with rows that
are not rows of the entity, so it is declared on the **result type** and the query stays a query:

```csharp
[QueryView<Reservation>]
[GroupBy<Reservation>(Properties = "Status")]
public partial class ReservationsByStatusView
{
    [From<Reservation>]                        public ReservationStatus Status { get; init; }
    [Count<Reservation>]                       public int Reservations { get; init; }
    [Sum<Reservation>(Expression = "Amount")]  public decimal Total { get; init; }
}

[Query<Reservation, ReservationsByStatusView>]                 // the query filters,
[Endpoint(HttpVerb.Get, "api/reservations/by-status")]         // the view groups
[RequirePermission(BookingPermissions.Reservation.Read)]
public partial class ReservationsByStatusQuery
{
    [Filter] public Guid? PropertyId { get; init; }
}
```

The grouping runs in the database, and the route, the permission and the invoker are the ones every
other query gets. Paged, the page is a page of **groups** and the total is the number of groups.

- A key may be a member the generator writes. `[GroupBy<LeaveRequest>(Properties = "TeamId")]` groups
  by the foreign key a `[Relation.*]` produces, and `Via = "AbsenceKind"` walks the navigation it
  produces. A key or `Via` that names nothing is **PRAG0732** (error).
- To group by a computed value, such as a month or a band, declare it `[Projectable]` on the entity
  and name it as a key. The key is the member's body, which the database computes; the getter is
  never read.
- `Expression` on `[Sum]`/`[Avg]`/`[Min]`/`[Max]` is written against the entity. `"Quantity * UnitPrice"`
  reads both members from the row, and a `[Projectable]` member is summed as its body.

⚠️ A `[QueryView]` on its own generates only a `Build(IQueryable<T>)` — no route, no entry in the
published contract. Naming it as a query's result is what gives an aggregate an HTTP surface.

### 7. GridFilter (runtime operator selection)

Use when the user selects the operator from the UI:

```csharp
[GridFilter<Order>]
public partial class OrderGridFilter
{
    [Filterable(Operators = FilterOps.String)]
    public string? OrderNumber { get; set; }
    public StringOperator? OrderNumberOperator { get; set; }   // Equals|Contains|StartsWith|...

    [Filterable(Operators = FilterOps.Range, MapTo = "Total")]
    public decimal? MinTotal { get; set; }

    [Sort(MapTo = "CreatedAt")]
    public SortDirection? CreatedAtSort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

### 8. Tri-state patch

```csharp
[Patch<Order>]
public partial class UpdateOrderPatch
{
    public decimal? Total { get; init; }
    public OrderStatus? Status { get; init; }
}
// Usage:
patch.ApplyPatch(order);   // applies only the properties that were set
```

`[Patch<TEntity>]` (namespace `Pragmatic.Persistence.Patch`) uses ordinary nullable properties + `MarkSet`/`SetProperties` tracking and applies with `ApplyPatch`. For an HTTP PATCH endpoint where you need to distinguish "field not sent" from "field explicitly null", use instead `[GeneratePatch<TEntity>]` from the **separate package `Pragmatic.Patch`**: it generates `Optional<T>` properties (tri-state `HasValue`/`IsUndefined`) and `ApplyTo`. Full comparison of both in `pragmatic-use-foundation`.

### 9. State machine

```csharp
public enum OrderStatus { [InitialState] Draft,
                          [TransitionFrom(OrderStatus.Draft)] Submitted,
                          [TransitionFrom(OrderStatus.Submitted)] Paid,
                          [TransitionFrom(OrderStatus.Draft)] [TransitionFrom(OrderStatus.Submitted)] Cancelled }

[Entity]
[StateMachine<OrderStatus>]
public partial class Order
{
    public OrderStatus Status { get; private set; }
}
// Usage — it never throws: the illegal transition comes back as a failed Result
VoidResult<IError> moved = order.TransitionTo(OrderStatus.Paid);
if (moved.IsFailure) { /* ConflictError, status 409, naming both states */ }

if (order.CanTransitionTo(OrderStatus.Cancelled)) { ... }
ReadOnlySpan<OrderStatus> next = order.AllowedTransitions();
```

### 10. Owned / scoped data

Row-level visibility without writing manual filters:

```csharp
[Entity] [HasOwner]                     // OwnerId auto-set, filter Priority 200
public partial class PrivateNote { ... }

[Entity] [HasAccessScopes]                    // AccessScopes JSON column, filter Priority 250
public partial class TeamDoc { ... }
```

Admin bypass: permission `{boundary}.{entity-kebab}.view-all` — the entity segment is the type name in
**kebab-case**, so `PrivateNote` → `sales.private-note.view-all`. Spelled any other way it never
matches, and the bypass silently does not apply.

The filters reach a collection of these rows wherever a query through the repository or the executor
reads it — an `Include`, `x.Notes.Any(…)`, `x.Notes.Count`, a projection or an aggregate — and a cached
answer that read one is cached per caller. A direct `DbSet` read is outside that pipeline.

An inserted row with no scopes is stamped with its creator's, `user:{id}`, by `ScopeInterceptor` at
`SaveChanges` — the same shape `[HasOwner]` uses. A row that already carries scopes keeps them, an
update is never re-stamped, and with no current user nothing is stamped.

Beyond that, scopes are granted through the generated `GrantScope(string)`, which is `internal`: only
the boundary that owns the entity can grant them, so the call belongs in that library's own code.
For more complex scope rules: `DataScopeRule<T>` registered with `services.AddDataScopeRule<TRule, TEntity>()` (see `pragmatic-use-authorization`).

### 11. Relations

```csharp
[Entity]
[Relation.OneToMany<OrderLine>]                          // 1 inferred nav
[Relation.ManyToOne<Customer>]
[Relation.ManyToMany<Tag>.WithNavigation("Tags", Inverse = "Orders")]
public partial class Order { ... }
```

Multiple relations to the same type? `WithNavigation("UniqueName")` disambiguates them — without it
**PRAG0612** (warning), and two navigations with one name are **PRAG0615**.
Cross-boundary? FK only, no navigation — and nothing reports it, so plan for it.

### 12. Declarations on the entity that generate more than a column

Five attributes that carry their weight and are easy to miss.

**`[Invariant]` — a rule about the whole aggregate.** A parameterless instance method returning
`bool` and **at least `internal`**. It runs after the operation's body and *before* persist; a `false`
rejects the operation with `InvariantViolationError` (HTTP 422). Which operations, exactly, is below —
it is not every write.

```csharp
[Invariant("An amenity cannot have more than 20 keywords", MessageKey = "error.too_many_keywords")]
internal bool KeywordsWithinLimit() => Keywords.Count <= 20;
```

⚠️ **`private` does not work, and writing a rule nobody calls from outside as `private` is the natural
mistake.** The invoker is a generated class beside the entity, so it can only call what the entity lets
its assembly call. Five conditions have to hold — parameterless, instance, returns `bool`, at least
`internal`, a name no other invariant of the entity uses — and failing any of them is **PRAG0463**, an
error that names which condition failed.

Use it for what must hold about the *whole* entity. Property attributes validate a field in
isolation; this validates the aggregate, and it is the last gate before the write **on that path**.

**`MessageKey` is how the refusal reaches the caller in their language.** It is an error key's base, as
every other error's is, so the translation files carry `error.too_many_keywords.title` (the sentence)
and `.detail` (what it explains); the `Message` above is what a host with no translation for the key
answers. Write it as a string, not as a `TKeys` constant — a base is a nested type in that class,
whose members are those two suffixes.

Without it the refusal reports `error.invariant.violation`, which **every** invariant in the
application shares: translating it means giving up which rule refused, so an application ends up
sending the English sentence to a caller who asked for another language.

**A `[PartOf]` child carries its own**, and the aggregate's invoker checks them after it has merged
it — which is the only write path a child has, so it is the only place they could be checked. Put the
rule about a line on the line.

**Which operations check it.** A `[Mutation]` checks every rule of the aggregate it writes. A
`[DomainAction]` that loaded the aggregate with `[LoadEntity]` checks the rules it **can answer** —
those whose body reads no navigation outside that action's `Include` list.

⚠️ That qualification is a measurement, not caution. An action includes what its own body needs, so a
rule over a navigation it left out would read an **empty collection**: on an invoice, voiding includes
the lines and not the payments, and a rule over the payments would compare a real amount against
nothing and refuse a void that is correct. Checking it would add a 422 to a request that should
succeed.

⚠️ **Still not every write.** A repository `Add`/`Remove` from a job or a seeding step runs no
invariant, and neither does an action that changes the aggregate without loading it through
`[LoadEntity]`. A rule that must hold everywhere is enforced by the entity's own method, which every
caller goes through.

**`[Audited]` on the entity** — records who changed what and when, without a line of your code: with
`Pragmatic.Audit.EFCore` referenced, the host registers `AuditDbContext` and `AddAuditTrail()` on the
entity's database, whose migration creates the trail's tables.

**`[GenerateHierarchy]` on a self-referencing entity** — generates `GetDescendants` /
`GetAncestors` as recursive CTEs, so a tree query is one call and one round trip instead of a loop.

**`[Published]` on a boundary's query** — declares it a read contract another boundary may consume,
and generates that contract. `ContractName` renames it. Without it, a cross-boundary read has no
declared shape.

**`[SearchAcross("Name", "Code", "Email")]` on a query or grid-filter property** — one search box,
several columns, joined by `||`:

```csharp
[SearchAcross(nameof(Property.Name), nameof(Property.City), IgnoreCase = true)]
public string? Search { get; init; }
```

`IgnoreCase = true` lowers both sides — without it PostgreSQL compares as written, so "rossi" does not
find "Rossi". On a property that is not a `string` it is **PRAG0703**.

And on a filter property, **`[ComplexFilter]`** receives a whole filter object as JSON in one query
parameter — the property's type must be a `[FilterDto<TEntity>]`, and the generated `Apply()` delegates
to that DTO's own `ApplyFilter`. It is how a rich client sends a nested AND/OR structure, not an escape
hatch to arbitrary code.

## Identifiers

| Helper | Example | When |
|---|---|---|
| `Guid7.New()` | `[Entity]` default | PK on Postgres/SQLite — timestamp + random, sortable |
| `Guid7.NewForSqlServer()` | same but byte-shuffled | PK on SQL Server (clustered index) |
| `ShortGuid.Encode(g)` | URL `/orders/abc123XYZ_-22ch` | Public URLs |
| `OpaqueId.Encode(seq)` | URL `/u/Hk29zP` on int auto-increment | Masking sequential IDs |
| `Slug.Generate("Hello World!")` | `"hello-world"` | URL slugs |

## The clock a read and a write evaluate against

Every generated repository takes an optional `TimeProvider` and uses it for two things: the audit
columns where the entity has them, and `FilterContext.Now` — the instant a temporal filter evaluates
against.

```csharp
// In a test host, so a recorded instant becomes a value you can name.
services.RemoveAll<TimeProvider>();
services.AddSingleton<TimeProvider>(pinned);
```

⚠️ **`FilterContext.Now` is `required` on purpose.** A default of `UtcNow` would let any read that
forgot it consult the wall clock silently — it could not fail and it could not be pinned. Required, "as
of when" is something a caller states.

⚠️ **Pinning it is what makes a temporal assertion a measurement.** Without it, every test asserting a
recorded time is a range — `BeCloseTo(UtcNow, 1 minute)` — which passes on a stamp taken from anywhere.
With it, the assertion is an equality, and moving the clock by a known amount is how you tell two
writes apart. Note the second part: with a frozen clock two writes moments apart carry the **same**
instant, so a test that distinguished them by "the column moved" needs `Advance` between them.

## When the database refuses

A unique index, a foreign key, a null or length constraint are rules **the application declared** —
`[LogicKey]`, a relation, `[Required]` — and the database is only where they are enforced. Violating
one does not surface as a provider exception:

```
POST /api/workspaces/members   →  409, {"title": "...", "status": 409}
```

The unit of work classifies the provider's exception and throws a
`PersistenceRuleViolationException` carrying an `IError`; the host's exception mapping renders it with
that error's status. An operation that wants to react — an import saying which row was refused —
catches it and reads `.Error`:

```csharp
catch (PersistenceRuleViolationException violation)
{
    rejected.Add(new RejectedMember(index, row.ExternalId, violation.Error.Code));
}
```

The base `Pragmatic.Result.EFCore` carries a provider-agnostic heuristic; adding
`Pragmatic.Result.EFCore.<Provider>` replaces it with the provider's own error codes.

**A delete a relation refuses is a conflict that says what holds the row.** Deleting a row that a
restricting relation still points at — `OnDelete = Restrict`, or a required reference left at its
default — answers `DbInUseError`: **409**, code `ENTITY_IN_USE`, with `entityType` (what was being
deleted) and `usedBy` (what references it) in the problem body. The names come from the model: the
violated constraint's foreign key, or the one relation into that type when the provider does not name the
constraint. Nothing needs checking by hand before the delete — the declared rule answers. A write that
names a *missing* row is still `DB_CONSTRAINT` (400): there the request is wrong.

```
DELETE /api/absence-kinds/{id}  →  409 {"code":"ENTITY_IN_USE","entityType":"AbsenceKind","usedBy":"Allowance"}
```

A Delete mutation exposed with `[Endpoint]` answers **204 and no body** unless it declares
`[ReturnsDto<T>]` (or a key `ReturnType`): the removed row is not an answer, and serialised it is every
public property the entity has.

This holds for a repository write too, not only for a mutation: the generated repository's
`SaveChangesAsync` goes through the boundary's unit of work — the same instance an invoker holds — so
it classifies, hands over the entity's domain events, and records the save the same way. A concurrency
conflict is the one thing not reframed: it stays a `DbUpdateConcurrencyException`, because a stale row
is two writers meeting rather than a rule the schema enforces, and a concurrency-aware entity's
repository answers it with `ConcurrencyError`.

⚠️ **A failed save leaves its entity tracked, and the next save retries it.** That is a change tracker,
not a defect — but it means one refused row would make every later commit of the same request fail for
the old reason. `IUnitOfWork.Detach(entity)` stops tracking it, and the mutation pipeline calls it on
every save path. Reach for it yourself only when you save through the unit of work directly: without
it, `[CommitStrategy(CommitMode.PerStep)]` cannot keep what came before the failure, which is the whole
of what it promises.

## Most frequent diagnostics

| ID | Sev | Trigger | Quick fix |
|---|---|---|---|
| **PRAG0600** | Error | `[Entity]` on a non-`partial` class | Add `partial` |
| **PRAG0602** | Error | A generated DbContext type is not `partial` | Add `partial` |
| **PRAG0620** | Error | `[StateMachine<>]` without `[InitialState]` | Mark one state |
| **PRAG0622** | Error | `[TransitionFrom]` names a non-existent enum member | Use a real member |
| **PRAG0680** | Warning | `new Order()` instead of factory | `Order.Create(...)` |
| **PRAG0684** | Warning | Non-constant SQL in `FromSqlRaw`/`ExecuteSqlRaw` | Use interpolated `FromSql`/`ExecuteSql` |
| **PRAG0686** | Warning | Injecting another boundary's `DbContext` | Call that boundary's actions/queries |
| **PRAG0711** | Warning | `[LoadWith]` depth greater than 3 | Reduce depth or use projection |
| **PRAG0730** | Error | A `[FromCurrentUser]` property with a `public`/`internal`/`init` setter | `{ get; private set; }` |
| **PRAG0731** | Error | A `[FromCurrentUser]` binding that cannot be generated (the message says why) | Name a member of the `[PragmaticUser]` entity, of the property's type; `string` for the member-less form |
| **PRAG0732** | Error | A `[GroupBy]` key or `Via` that names no member of the entity | Name a declared or generated member; reach another entity with `Via` |
| **PRAG0733** | Error | A `[ComputedFilter]` the generator cannot write (not `bool`, static, generic, block body, `ref`/`out`/`in`/`params` parameter) | A `bool` property or instance method with an expression body |
| **PRAG0735** | Error | A specification in a `[Projectable]`/`[ComputedFilter]` body takes a value from the row | Pass constants or a filter method's parameters; write a row-dependent condition as a lambda |
| **PRAG0734** | Error | A `[FromClock]` binding that cannot be generated | `DateOnly` or `DateTimeOffset`, `{ get; private set; }` |
| **PRAG1100** | Error | `[HasOwner]` not `partial` | Add `partial` |

When the build produces multiple diagnostics, fix Error ones first — cascading Warnings often disappear.

## Troubleshooting

**Generated files missing after clean build**
1. Is `Pragmatic.SourceGenerator` referenced as an analyzer? Verify `<IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>`.
2. Stale cache: `dotnet nuget locals all --clear; dotnet restore; dotnet build`.
3. Inspect: `obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/`.

**`IRepository<T>` not resolved in DI**
- Does `PragmaticDatabase` exist in the Host project?
- Is the boundary library referenced by the host?
- Is the entity in a namespace captured by `[Module(Name=...)]` or an inferred sub-boundary?

**Generic attribute not detected by the SG**
- An attribute that takes a type takes it as a generic argument — `[BelongsTo<SalesBoundary>]`, never
  `typeof`; the typeof form produces no match. `[Entity]` takes none: the key is always a `Guid`.

**Cross-boundary relation does not generate navigation**
- This is intentional and silent. Keep the FK, perform joins via query/projection or through an orchestrating mutation.

**Soft-deleted records not visible even to admins**
- ⚠️ `repo.Query().IgnoreQueryFilters()` does **not** reveal them. That removes EF Core's own named
  filter — the safety net for querying the `DbSet` directly — while the Pragmatic soft-delete filter is
  a `Where` the repository applies on top.
- The ways that work: `filterToggle.Disable<{Entity}.SoftDeleteFilter>()`,
  `filterToggle.UseMode(FilterMode.Raw)`, or `repo.Query(QueryStrategy.Raw)`.
- `FilterMode.Admin` skips visibility and permission filters, **not** soft-delete.

## Build verification

```powershell
dotnet build                                                # no PRAG = topology OK
dotnet test path\to\MyApp.Tests --no-restore -v minimal    # if you have integration tests
ls obj\Debug\net10.0\generated\Pragmatic.SourceGenerator   # inspect SG output
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing example application — code
that compiles and that `Invoicing.IntegrationTests` exercises — and kept identical to it by the gate: an
aggregate and its child, the state machine, soft delete, uniqueness and a generated code, a declarative
paged query, an aggregate query over a view, and specifications.
