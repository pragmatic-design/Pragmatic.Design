---
title: "Query System"
description: "> Declarative queries, filter DTOs, and paged results, all source-generated from attributes."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/09-query-system.md
sidebar:
  order: 10
---
> Declarative queries, filter DTOs, and paged results, all source-generated from attributes.

## The Problem

Querying data in a typical .NET application involves writing repetitive `IQueryable` chains: filter by this, sort by that, include these navigations, project to this DTO, page the results. For 30 entities, you write 30 nearly identical query classes, each with the same pattern of `Where`, `OrderBy`, `Select`, `Skip`, `Take`.

```csharp
// The typical approach: manual query building
public async Task<PagedResult<OrderDto>> SearchOrders(
    string? customerName, OrderStatus? status, DateTimeOffset? fromDate,
    SortDirection? orderDateSort, int page = 1, int pageSize = 20)
{
    var query = db.Orders.AsNoTracking();

    if (customerName is not null)
        query = query.Where(o => o.CustomerName.Contains(customerName));
    if (status is not null)
        query = query.Where(o => o.Status == status);
    if (fromDate is not null)
        query = query.Where(o => o.OrderDate >= fromDate);

    if (orderDateSort == SortDirection.Ascending)
        query = query.OrderBy(o => o.OrderDate);
    else if (orderDateSort == SortDirection.Descending)
        query = query.OrderByDescending(o => o.OrderDate);

    var total = await query.CountAsync();
    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(o => new OrderDto { /* map every property */ })
        .ToListAsync();

    return PagedResult<OrderDto>.Success(items, total, page, pageSize);
}
```

For every query endpoint, you repeat the same null-check-then-filter pattern. For every DTO, you hand-write the projection. For every sortable field, you write the `if ascending / else descending` branch. With 30 entities and multiple query endpoints each, this becomes thousands of lines of mechanical code.

## The Solution: Declarative Queries

A **query** is a class that declares what to filter, sort, and project; the source generator produces the `Apply()` method and the interface implementation.

```csharp
// ═══ What YOU write ═══
[Query<Order, OrderDto>]
public partial class GetOrders
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? CustomerName { get; init; }

    [Filter]
    public OrderStatus? Status { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]   // descending unless the caller says otherwise
    public SortDirection? OrderDateSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

The source generator reads the `[Query]`, `[Filter]` and `[Sort]` attributes and produces the
`Apply()` method, the `Projection` expression and the right interface implementation.

⚠️ **The result DTO needs two attributes, not one.** `[MapFrom<TEntity>]` gives it `FromEntity` and
`Selector`, which map an object already in memory; the `Projection` the generated `Apply` names comes
from **`[GenerateProjection]`**. Declare only the first and the query is **PRAG0704**:

```csharp
[MapFrom<Order>]
[GenerateProjection]
public partial class OrderDto { … }
```

```csharp
// ═══ What the SOURCE GENERATOR produces ═══
public partial class GetOrders : IPagedQuery<Order, OrderDto>
{
    public Expression<Func<Order, OrderDto>>? Projection => OrderDto.FromEntity;

    public IQueryable<Order> Apply(IQueryable<Order> query)
    {
        if (CustomerName is not null)
            query = query.Where(e => e.CustomerName.Contains(CustomerName));
        if (Status is not null)
            query = query.Where(e => e.Status == Status);

        // DefaultDirection = SortDirection.Descending → applied even when OrderDateSort is null
        var orderDateDir = OrderDateSort ?? SortDirection.Descending;
        query = orderDateDir == SortDirection.Ascending
            ? query.OrderBy(e => e.OrderDate)
            : query.OrderByDescending(e => e.OrderDate);

        return query;
    }
}
```

The SG detects `Page` and `PageSize` properties by convention and implements `IPagedQuery<Order, OrderDto>` (which provides `Skip` and `Take` automatically). If those properties are absent, it implements `IQuery<Order, OrderDto>` instead.

### Which properties become filters

Three forms generate a filter. A fourth generates nothing, and it is the one that reads as though it
should:

| Declaration | What is generated |
|---|---|
| `public required Guid CustomerId { get; init; }` | **always applied**: `query.Where(e => e.CustomerId == this.CustomerId)` |
| `public OrderStatus? Status { get; init; }` | applied when it has a value, guarded by `is not null` |
| `[Filter] public Guid CustomerId { get; init; }` | applied when it is **not `default`**, guarded by `!= default` |
| `[FromCurrentUser(nameof(Customer.Id))] public Guid CustomerId { get; private set; }` | **always applied**, and filled from the caller; see [Filtering by the caller](#filtering-by-the-caller-fromcurrentuser) |
| `[SearchAcross("Name", "Email", IgnoreCase = true)] public string? Search { get; init; }` | applied when it has a value, against **each** column named, joined by `\|\|`; see [Searching several columns](#searching-several-columns-searchacross) |
| `[FromClock] public DateOnly Today { get; private set; }` | **no filter**: filled from the clock for a specification to read; see [Reading the clock](#reading-the-clock-fromclock) |
| `public Guid CustomerId { get; init; }` | ⚠️ **nothing** |

The rule behind the table: a property is a filter when it carries `[Filter]`, **or** it is `required`,
**or** it is `[FromCurrentUser]`, **or** it is nullable and is not a sort or paging property.

⚠️ **A property that is none of the three produces no filter and no diagnostic.** `Apply` returns the
query untouched, `ToSpecification` returns `Spec.True`, and a `Single = true` query written that way
answers `200` with whichever row comes first. It is the form you reach for on a route id
(`GET /orders/{id}` with `public Guid Id { get; init; }`) because the id *is* mandatory, and it is the
one that does nothing.

⚠️ Prefer `required` over `[Filter]` on a mandatory scalar. The `[Filter]` form is guarded by
`!= default`, so `Guid.Empty` (or `0`, or `DateTime.MinValue`) means "do not filter" rather than
"look for this value". On a route id that turns a malformed request into a full scan.

⚠️ **Do not filter on the entity's `Id`.** It is a read-only alias the traits generator writes beside
`PersistenceId`, and it is **not mapped**: projecting it works (every generated DTO does), but a
`Where` on it fails to translate at runtime, with an EF message about an unmapped member. The column is
`PersistenceId`:

```csharp
[Filter(MapTo = "PersistenceId")]
public required Guid Id { get; init; }
```

The two compound. With the plain form there is no filter, so the query never reaches EF and the
translation error never appears; making the property `required` is what surfaces it.

### Reusing a rule instead of restating it

A filter names a column and a comparison. A **rule** is a sentence with meaning ("confirmed, and of the
kind the slot expects"), and restating it in every query is how one of them ends up saying something
slightly different. Write it once as a `Specification<TEntity>` and let the query compose it:

```csharp
[Query<KnowledgeItem, KnowledgeItemDto>]
public partial class SuggestTermsQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Term { get; init; }

    [BindSpecification]
    public KnowledgeKind? Kind { get; init; }

    public Specification<KnowledgeItem> Rule =>
        Kind is { } kind ? KnowledgeItemSpecifications.Suggestable(kind) : KnowledgeItemSpecifications.Confirmed();
}
```

Two moving parts, and each says one thing:

- **The specification is recognised by its type.** Any property of type `Specification<TEntity>` is
  applied whole, in `Apply()` and in `ToSpecification()`, so the query answers the same through the
  runner and through the repository. `null` is skipped, so a rule that only sometimes applies simply
  answers null when it does not. A rule that reads no input is written `static` (the analyzers ask
  for it, CA1822) and is applied the same way:
  `public static Specification<LeaveRequest> Approved => Spec<LeaveRequest>.Where(r => r.Status == LeaveRequestStatus.Approved);`
- **`[BindSpecification]` marks the input it reads.** A specification cannot be bound from a request:
  there is no way to deserialize a predicate, so what crosses the wire is the value the rule is built
  from. The attribute says that value is an *input*, not a filter: without it a nullable property would
  generate a `Where` **and** feed the specification, applying the same rule twice, and a plain
  non-nullable one would be `PRAG0707`.

⚠️ **This is not for ordinary filters.** `[Filter] Guid? GuestId` is shorter and clearer than any
specification of the same predicate, and replacing one with the other buys indirection. Reach for a
specification when the predicate has a name worth writing down and more than one caller.

### The other direction: a specification that becomes the query

A specification can carry `[Query]` itself. The generator derives a query type from it (the rule
becomes the query's `Rule` property, and the specification's parameters become the query's inputs),
and `[Endpoint]` beside it gives that derived query a route.

```csharp
public static partial class ReservationSpecifications   // the entity's generated partial
{
    [Query<Reservation, ReservationSummaryDto>]
    [RequirePermission(BookingPermissions.Reservation.Read)]
    [Endpoint(HttpVerb.Get, "api/reservations/confirmed")]     // opt-in: no attribute, no route
    public static Specification<Reservation> IsConfirmed()
        => WithStatus(ReservationStatus.Confirmed);
}
```

- **The specification is not modified.** A new type holds the paging, the route and the projection, and
  holds the rule as a property; adding `Page`/`PageSize` to a predicate would destroy what makes it
  composable.
- **The route is opt-in on top of the query.** A rule that declares no `[Endpoint]` stays executable
  in-process and publishes nothing; deriving a path from the member's name would put a URL nobody chose
  into the contract. `[Endpoint]` on a member nothing derives from is `PRAG0525`.
- **The permission may be a generated constant.** It cannot bind while the compilation is being
  analysed, so it travels as a path and is resolved against the permission catalog, the same route the
  attribute takes on a hand-written operation.

Where a hand-written query reaches the same rows by declaring a filter, the two agree: one rule, two
ways of reading it.

**Composition is AND**, like every other contribution: each is its own `Where`. An alternative belongs
*inside* one property, where the reader can see it:

```csharp
public Specification<Order>? Visible => MineOnly ? OrderSpecifications.Mine(user) | OrderSpecifications.Delegated(user) : null;
```

That is the same rule a `[FilterDto]` follows: its groups may be OR internally, and the group as a whole
is still ANDed with the rest.

⚠️ `PRAG0709` checks the half of the claim that can be checked: a query with a `[BindSpecification]`
input and no `Specification<T>` property at all is an error. Marking an input and then never writing the
specification would leave the value read and dropped **with a declaration standing over it saying
otherwise**, quieter than the silence the attribute exists to lift.

### Required filters

Properties marked `required` are always applied, since they cannot be null:

```csharp
[Query<Order, OrderDto>]
public partial class GetOrdersForCustomer
{
    // Required → always included in the WHERE clause
    public required Guid CustomerId { get; init; }

    // Optional → applied only when not null
    [Filter]
    public OrderStatus? Status { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

```csharp
// ═══ Generated Apply ═══
public IQueryable<Order> Apply(IQueryable<Order> query)
{
    query = query.Where(e => e.CustomerId == CustomerId);  // Always applied
    if (Status is not null)
        query = query.Where(e => e.Status == Status);
    return query;
}
```

### Filtering by the caller: `[FromCurrentUser]`

"My orders", "my balances", "my profile": a read whose filter is **who is asking**. As a public input it
would be a value the caller sends, so a caller could send someone else's. `[FromCurrentUser]` (in
`Pragmatic.Identity`, from `Pragmatic.Abstractions`) makes it a property the generated invoker fills:

```csharp
[Query<Allowance, AllowanceBalanceDto>]
[RequirePermission(ManageOwnProfile.Value)]
[Endpoint(HttpVerb.Get, "api/me/balances")]
public partial class GetMyBalancesQuery
{
    [FromCurrentUser(nameof(Employee.Id))]     // a member of the [PragmaticUser] entity
    public Guid EmployeeId { get; private set; }

    [Filter]
    public required int Year { get; init; }
}
```

Two forms:

| Declaration | What the invoker writes | Property type |
|---|---|---|
| `[FromCurrentUser]` | `ICurrentUser.Id` | `string` |
| `[FromCurrentUser(nameof(Employee.Id))]` | that member of the application's `[PragmaticUser]` entity, read through its generated `{User}Resolver` | the member's type |

- **The invoker fills it, after validation and the permission check, before the read**; see
  [the query pipeline](/modules/persistence/15-query-pipeline/). A caller who is not authenticated gets `UnauthorizedError`
  (401); an authenticated caller with no user entity gets `NotFoundError` (404).
- **It is not a parameter anywhere.** Not a query-string or route parameter, not in the OpenAPI document,
  not an argument of the boundary interface. The property is `{ get; private set; }`: the generated
  nested `Invoker` reaches the setter, and nobody outside the type does. A route placeholder that names it
  binds nothing and is reported by `PRAG0504`.
- **It is still a property**, so it is part of the `[Cacheable]` key and of the serialized query: two
  callers never share an entry.
- **It is a filter, always applied and matched exactly.** No `[Filter]` is needed (`[Filter(MapTo = …)]`
  still names another column), it is never skipped at its default, and a `string` is compared with `==`
  (a `Contains` on an id would read the rows of every caller whose id contains yours). `[BindSpecification]`
  hands it to a specification instead, as for any other input.
- **The invoker constructs the resolver.** The generator writes `{User}Resolver` in the same compilation
  and knows its constructor (`IReadRepository<TUser>`, `ICurrentUser`), so nothing depends on it being
  registered. The member form therefore needs the `[PragmaticUser]` entity in the same compilation as
  the query, and `Pragmatic.Identity.Persistence` referenced, which is what generates the resolver.
- **Scope: `[Query]`.** An action composes and can inject `ICurrentUser`; a mutation's target id is
  generated, and binding it is a separate design.

Two diagnostics hold the form:

| Id | When |
|---|---|
| `PRAG0730` | the property can be set by its caller: a `public`, `internal` or `init` setter. The invoker overwrites it, so it reads as an input and is not one |
| `PRAG0731` | the binding cannot be generated: the member does not exist on the user entity, its type differs from the property's, `nameof` names a member of another type, there is no `[PragmaticUser]` entity (or its resolver is not generated) in this compilation, or the member-less form is on a property that is not a `string` |

⚠️ **Why not inject `ICurrentUser` into the query.** A query's answer must be a function of its
properties: `[Cacheable]` builds the key from them, the remote boundary serializes them, and the
contract is read off them. A service that changes the answer is an input none of those can see: the
`GetMyOrders` case in `Pragmatic.Caching/docs/common-mistakes.md` §6, where every user shares one key.

### Searching several columns: `[SearchAcross]`

One search box, several columns: the value is looked for in each column named, and a row matches when
any of them contains it.

```csharp
[SearchAcross(nameof(Employee.FullName), nameof(Employee.WorkEmail), nameof(Employee.EmployeeNumber),
    IgnoreCase = true)]
public string? Search { get; init; }

// Generated, in Apply and in ToSpecification:
// e => (e.FullName != null && e.FullName.ToLower().Contains(this.Search!.ToLower()))
//   || (e.WorkEmail != null && e.WorkEmail.ToLower().Contains(this.Search!.ToLower()))
//   || (e.EmployeeNumber != null && e.EmployeeNumber.ToLower().Contains(this.Search!.ToLower()))
```

- **`IgnoreCase`** lowers both sides, the shape `[Filter(IgnoreCase = true)]` uses. Without it the
  comparison is the provider's (on PostgreSQL case-sensitive), and a column lowered cannot use an
  ordinary index on it.
- The property is text: `[SearchAcross]` on anything else is `PRAG0703`. The same attribute on a
  `[GridFilter<T>]` searches the same way, `IgnoreCase` included.

### Reading the clock: `[FromClock]`

"Who is away today", "what is overdue now": a read whose value is the date. Taken from the caller,
anyone could choose which day is today; written as `DateTime.UtcNow` inside a rule, it is the database's
clock, not the application's. `[FromClock]` (in `Pragmatic.Temporal.Clock`, from
`Pragmatic.Abstractions`) makes it a property the generated invoker fills from the registered `IClock`,
the one the mutations that decide and withdraw read, through the same attribute:

```csharp
[FromClock]
public DateOnly Today { get; private set; }          // IClock.UtcToday; a DateTimeOffset gets UtcNow

[BindSpecification]
public bool? AwayToday { get; init; }

public Specification<Employee>? WhoIsAwayToday => AwayToday switch
{
    true => EmployeeComputedFilters.IsAwayOnSpec(Today),    // a [ComputedFilter] method on the entity
    false => !EmployeeComputedFilters.IsAwayOnSpec(Today),
    null => null
};
```

- **The invoker writes it**, after validation and the permission check, before the read, from
  `GetRequiredService<IClock>()`: a host without a clock is a configuration error, not a silent wall
  clock. Temporal registers one.
- **It is not a parameter and not a filter.** Not in the query string, the route, OpenAPI or the
  boundary interface; a specification of the query reads it. It is still a property, so it is part of
  the `[Cacheable]` key: yesterday's answer is not today's.
- `PRAG0734`: a type the clock does not give (anything but `DateOnly` and `DateTimeOffset`), or a setter
  another caller can reach; it is `{ get; private set; }`.
- **Not only on a query.** `[FromClock]` and `[FromCurrentUser]` mean the same on a `[DomainAction]` and a
  `[Mutation]`: the invoker writes them after validation and authorization, before `Execute`/`ApplyAsync`
  and the `[LoadEntity]` preload, and the diagnostics are the same. On a mutation the value is also
  written to an entity member of the same name, when there is one.

## Query Without Projection

When you don't need to project to a DTO, use the single-type-argument form. This returns the entity itself:

```csharp
[Query<Order>]
public partial class FindOrders
{
    [Filter]
    public Guid? CustomerId { get; init; }
}
```

The SG implements `IQuery<Order>` (no projection). The executor returns entities directly.

## Using Queries

Queries are executed through `IQueryExecutor`:

```csharp
public class OrderService(IQueryExecutor executor, IRepository<Order> orders)
{
    public async Task<PagedResult<OrderDto>> SearchOrders(GetOrders query, CancellationToken ct)
    {
        var source = orders.Query();   // IQueryable<Order>
        return await executor.ExecuteAsync(query, source, ct);
    }
}
```

When combined with an `[Endpoint]` attribute, the query is executed automatically by the endpoint pipeline; you don't need to write the service method at all:

```csharp
// This is a fully functional API endpoint. No service class needed.
[Query<Reservation, ReservationSummaryDto>]
[Endpoint(HttpVerb.Get, "api/v1/reservations/search")]
public partial class SearchReservationsQuery
{
    [Filter]
    public Guid? GuestId { get; init; }

    [Filter]
    public ReservationStatus? Status { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? CheckInSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

## `[Filter]`: Filter Properties

The `[Filter]` attribute marks a property as a filter condition. When the property value is null, the filter is skipped. When it has a value, the corresponding `Where` clause is added.

```csharp
// Basic filter: Equals operator (default for non-strings)
[Filter]
public OrderStatus? Status { get; init; }

// String filter: Contains operator (default for strings)
[Filter]
public string? Name { get; init; }

// Explicit operator
[Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "CreatedAt")]
public DateTimeOffset? FromDate { get; init; }

// Collection-based filter: IN operator
[Filter(Operator = FilterOperator.In)]
public List<OrderStatus>? Statuses { get; init; }

// Case-insensitive string comparison
[Filter(IgnoreCase = true)]
public string? Email { get; init; }

// Nested property path
[Filter(MapTo = "Customer.Name")]
public string? CustomerName { get; init; }
```

### Filter Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Operator` | `FilterOperator` | `Equals` (non-string), `Contains` (string) | The comparison operator |
| `MapTo` | `string?` | Same as property name | Target property path on the entity |
| `IgnoreCase` | `bool` | `false` | Case-insensitive string comparison |

### Available Operators

| Operator | SQL Equivalent | Typical Use |
|----------|---------------|-------------|
| `Equals` | `= value` | Exact match (ID, status, boolean) |
| `NotEquals` | `!= value` | Exclusion filter |
| `Contains` | `LIKE '%value%'` | Text search |
| `StartsWith` | `LIKE 'value%'` | Prefix search |
| `EndsWith` | `LIKE '%value'` | Suffix search |
| `GreaterThan` | `> value` | Range (dates, amounts) |
| `GreaterOrEqual` | `>= value` | Range inclusive |
| `LessThan` | `< value` | Range (dates, amounts) |
| `LessOrEqual` | `<= value` | Range inclusive |
| `In` | `IN (values)` | Multi-select (statuses, categories) |

⚠️ **`FilterOperator.Between` is declared and not implemented, and using it is a build error**
(**PRAG0701**). No renderer has a branch for it: without the error the filter would fall back to `==`
and compare for equality without saying so, or (on the collection a range needs) produce a generated
file that does not compile. Express a range as two properties over one column:

```csharp
[Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Total")]
public decimal? MinTotal { get; init; }

[Filter(Operator = FilterOperator.LessOrEqual, MapTo = "Total")]
public decimal? MaxTotal { get; init; }
```

⚠️ **The same fallback catches a mismatched operator.** `Contains`, `StartsWith` and `EndsWith` are
rendered only when the property is a `string`, and `In` only when it is a collection; anywhere else the
switch ends in `==`. A `[Filter(Operator = FilterOperator.Contains)]` on an `int?` therefore compiles,
runs, and filters by equality; check the operator against the property's type, because nothing else
will.

## Filtering on a joined entity

There is no separate attribute for it. `[Filter]` takes a **path** in `MapTo`, and a path that crosses a
navigation filters on the entity at the far end:

```csharp
[Query<Order, OrderDetailDto>]
[Join<Customer>(Via = "Customer")]
public partial class GetOrdersByCustomerName
{
    // On the root entity
    [Filter]
    public OrderStatus? Status { get; init; }

    // On the joined entity, by path
    [Filter(MapTo = "Customer.Name", Operator = FilterOperator.Contains)]
    public string? CustomerName { get; init; }
}
```

`Operator`, `IgnoreCase` and the rest behave exactly as on a root-level filter; the path only decides
what the comparison is applied to.

## `[FilterGroup]`: Grouped Filter Logic

By default, all filters are combined with AND logic. When you need OR logic between a set of filters, group them using `[FilterGroup]`:

```csharp
[Query<Product, ProductDto>]
public partial class SearchProducts
{
    [Filter]
    public bool? IsActive { get; init; }

    // Creates: WHERE IsActive = X AND (Name LIKE '%..%' OR Description LIKE '%..%')
    [FilterGroup(FilterLogic.Or)]
    public ProductTextSearch? Search { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

[FilterDto<Product>]
public partial class ProductTextSearch
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Filter(Operator = FilterOperator.Contains)]
    public string? Description { get; init; }
}
```

⚠️ **`[FilterGroup]` is read inside a `[FilterDto<T>]`, not on a `[Query]`**; on a query it is
**PRAG0703**. The example above is therefore wrong as written: the attribute is ignored, the property
name is used as an entity column, and the generated file does not compile. The shape that works nests one filter DTO
inside another:

```csharp
[FilterDto<Product>]
public partial class ProductFilter
{
    [Filter]
    public bool? IsActive { get; init; }

    [FilterGroup(FilterLogic.Or)]                 // on a property OF a filter DTO
    public ProductTextSearch? Search { get; init; }
}
```

Filters inside the group combine with the given logic (`And` or `Or`); the group joins the rest with
AND, giving `WHERE (IsActive = @p0) AND (Name LIKE '%@p1%' OR Description LIKE '%@p1%')`.

On a **query**, the attribute that carries a whole filter object is `[ComplexFilter]`, below.

## `[ComplexFilter]`: JSON Complex Filters

For rich client UIs that need to send structured filter objects, use `[ComplexFilter]`. The property value is deserialized from a JSON query parameter:

```csharp
[Query<Property, PropertySummaryDto>]
[Endpoint(HttpVerb.Get, "api/properties/search")]
public partial class SearchPropertiesQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    /// <summary>
    /// Sent as JSON: ?Location={"cityGroup":{"city":"Rome"},"maxStarRating":4}
    /// </summary>
    [ComplexFilter]
    public PropertyLocationFilter? Location { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

[FilterDto<Property>]
public partial class PropertyLocationFilter
{
    [FilterGroup(FilterLogic.Or)]
    public CityGroupFilter? CityGroup { get; init; }

    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "StarRating")]
    public int? MaxStarRating { get; init; }
}
```

The SG generates an `Apply()` that delegates to the filter DTO's generated `ApplyFilter()` extension method:

```csharp
// Inside the generated Apply()
if (Location is not null)
    query = PropertyLocationFilterExtensions.ApplyFilter(query, Location);
```

The JSON deserialization is handled by `JsonQueryConverter<T>`, which is wired automatically for `[ComplexFilter]` properties.

## `[Sort]`: Sorting

The `[Sort]` attribute declares sortable fields. The property type is `SortDirection?`, and null means "don't sort by this field":

```csharp
// Dynamic sort: user chooses direction, no sorting if null
[Sort]
public SortDirection? CustomerNameSort { get; init; }

// Default sort: always applied (Descending), user can override
[Sort(DefaultDirection = SortDirection.Descending)]
public SortDirection? CreatedAtSort { get; init; }

// Map to a different entity property
[Sort(MapTo = "CreatedAt")]
public SortDirection? DateSort { get; init; }

// Multi-sort: Priority controls the order (lower = applied first)
[Sort(Priority = 0, DefaultDirection = SortDirection.Ascending)]       // Primary: Name Ascending
public SortDirection? NameSort { get; init; }

[Sort(Priority = 1, DefaultDirection = SortDirection.Descending)]        // Secondary: Date Descending
public SortDirection? DateSort { get; init; }
```

### Sort Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MapTo` | `string?` | Derived from property name (removes "Sort" suffix) | Target entity property |
| `DefaultDirection` | `SortDirection?` | unset (no default) | `SortDirection.Ascending` / `SortDirection.Descending`; unset means the sort applies only when the property carries a value |
| `Priority` | `int` | `0` | Sort order when multiple sorts are active (lower = first) |

The SG derives the entity property name from the query property name by removing the "Sort" suffix: `CustomerNameSort` maps to `CustomerName`, `CreatedAtSort` maps to `CreatedAt`. Use `MapTo` to override this convention.

## `[Join<TTarget>]`: Declaring Joins

`[Join<T>]` has **two forms**, and `ForeignKey` is what decides which one you are writing.

**Reach for it only when nothing else answers.** `[Filter(MapTo = "Customer.Name")]` already filters
across a navigation and `[GenerateProjection]` already flattens across one; both become a JOIN in
the SQL EF emits, with no `[Join]` anywhere. What a declared join adds is the two cases they cannot
reach: an entity related **by key with no navigation between the two**, and an **outer** join whose
unmatched rows have to survive.

### Form 1: `Via`, a navigation to load

```csharp
[Query<Order, OrderSummaryDto>]
[Join<Customer>(Via = "Customer")]
[Join<OrderLine>(Via = "Lines")]
public partial class GetOrderWithDetails
{
    [Filter(MapTo = "Customer.Name")]
    public string? CustomerName { get; init; }
}
```

The path joins the query's `IncludePaths`, which the executor applies before `Apply` runs, so this
says exactly what `[EagerLoad("Customer")]` says, with a type argument that documents the target.
Like an include, it does nothing on a query that projects: EF Core drops includes once the query no
longer returns the entity.

### Form 2: `ForeignKey`/`TargetKey`, an entity no navigation reaches

```csharp
// Order carries CustomerId and no navigation: Include cannot express this at all.
// ⚠️ Customer must be in this boundary's model: its own entity, or one the boundary declares
//    [ReadAccess<Customer>] to. Anything else is PRAG0742, at the declaration.
[Query<Order, OrderRowDto>]
[Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id", Type = JoinType.Left)]
public partial class GetOrders
{
    [Filter]
    public string? Reference { get; init; }
}

public sealed class OrderRowDto
{
    public string Reference { get; init; } = "";      // Order.Reference
    public string CustomerName { get; init; } = "";   // Customer.Name, through the join
}
```

The joined entity's columns cannot travel in a `Projection` (that is one entity in and one result
out), so a key join generates **`Aggregate`** instead: the filtered set in, the projected set out.
The target's `IQueryable` arrives from the executor through `IJoiningQuery.BindJoinSources`, which
reads the entity's own `IReadRepository<T>`, so a joined set carries the same tenant, soft-delete and
global filters as a direct read.

**How a result property finds its source**, in this order: the entity first (`Reference` is the
order's even if the customer has one too), then each join in declaration order, and within a join the
**prefixed** name before the bare one: `CustomerName` → `Customer.Name`. A name neither side answers
is **PRAG0740**, at your declaration instead of a `CS0117` inside a generated file.

### Join Properties

| Property | Type | What it does |
|----------|------|---|
| `Via` | `string?` | The navigation to load, resolved against the entity segment by segment; a name that is not a navigation is **PRAG0737**. The path joins `IncludePaths`. |
| `ForeignKey` + `TargetKey` | `string?`, `string` | The key join. `ForeignKey` is read on the query's entity, `TargetKey` on the joined type (default `"Id"`); a name that resolves on neither is **PRAG0739**. Declaring one on a query whose result **is** the entity is **PRAG0738**: the joined columns would have nowhere to go. ⚠️ The target has to be in the query boundary's own model (its entity, or one the boundary declares `[ReadAccess<T>]` to), because EF Core composes a join only inside one `DbContext` instance and a host builds one per boundary. Anything else is **PRAG0742**. |
| `Type` | `JoinType` | `Inner` (the default), `Left` (`GroupJoin` + `DefaultIfEmpty`, so the row with no match survives and its joined fields take the type's default) and `Cross`. `Right` and `Full` are **PRAG0741**: EF Core has no LINQ spelling for a FULL OUTER JOIN, and the step receives the root set already filtered, sorted and paged, so rows the root's filters never selected cannot be added back. ⚠️ On a `Via` join `Type` still says nothing (an include has no join type), and that is **PRAG0703**. |
| `Alias` | `string?` | The prefix that addresses this join's columns in the result, so two joins to the same type are told apart: `Alias = "Biller"` makes `BillerName` read `Customer.Name` from that one. ⚠️ On a `Via` join it says nothing, which is **PRAG0703**. |

## `[FilterDto<TEntity>]`: Standalone Filter DTOs

When you need reusable filter logic without a full query (for example, to share the same filter across multiple queries or to use it directly in service methods), use `[FilterDto<T>]`:

```csharp
[FilterDto<Order>]
public partial class OrderFilter
{
    [Filter]
    public OrderStatus? Status { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Total")]
    public decimal? MinTotal { get; init; }
}
```

The SG generates an extension method:

```csharp
// ═══ Generated: OrderFilterExtensions.g.cs ═══
public static class OrderFilterExtensions
{
    public static IQueryable<Order> ApplyFilter(
        this IQueryable<Order> query, OrderFilter filter)
    {
        if (filter.Status is not null)
            query = query.Where(e => e.Status == filter.Status);
        if (filter.MinTotal is not null)
            query = query.Where(e => e.Total >= filter.MinTotal);
        return query;
    }
}
```

Use it anywhere you have an `IQueryable<Order>`:

```csharp
var filter = new OrderFilter { Status = OrderStatus.Pending };
var pending = await db.Orders.ApplyFilter(filter).ToListAsync(ct);
```

Filter DTOs are also the building block for `[FilterGroup]` and `[ComplexFilter]`: the nested DTO must be a `[FilterDto<T>]`.

## Query Interfaces

The query system is built on a small interface hierarchy. The SG picks the right combination based on what your query class declares:

| Interface | Purpose |
|-----------|---------|
| `IQuery<TEntity>` | Base: has `Apply(IQueryable<TEntity>)` that builds the pipeline |
| `IQuery<TEntity, TResult>` | Adds `Projection` expression for `Select()` |
| `IPagedQuery<TEntity>` | Adds `Page`, `PageSize`, `Skip`, `Take` |
| `IPagedQuery<TEntity, TResult>` | Paged query with projection |
| `IIncludableQuery<TEntity>` | Declares eager-loading paths via `IncludePaths` |
| `IQueryHints` | Execution hints: `NoTracking`, `SplitQuery`, `IgnoreGlobalFilters` |

Every generated query also gets a `ToSpecification()` returning `Specification<TEntity>`: the same
filters as a specification, for the repository methods that take one (`FindAsync`, `CountAsync`,
`ExistsAsync`) rather than the executor. It carries the `Where`, not the sort, the paging or the
projection.

### How the SG Chooses the Interface

| Your Query Class Has | Generated Interface |
|---------------------|---------------------|
| `[Query<Order>]` | `IQuery<Order>` |
| `[Query<Order, OrderDto>]` | `IQuery<Order, OrderDto>` |
| `[Query<Order>]` + `Page`/`PageSize` | `IPagedQuery<Order>` |
| `[Query<Order, OrderDto>]` + `Page`/`PageSize` | `IPagedQuery<Order, OrderDto>` |

### Combining Mixin Interfaces

`IIncludableQuery<T>` and `IQueryHints` are mixin interfaces, which you implement alongside the base query:

```csharp
[Query<Order, OrderDto>]
public partial class GetOrders : IQueryHints
{
    public bool NoTracking => true;         // Default: true (read-only)
    public bool SplitQuery => true;         // Avoid cartesian explosion
    public bool IgnoreGlobalFilters => false;

    [Filter]
    public OrderStatus? Status { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

`IQueryHints` uses default interface members, so you only override what you need:

| Hint | Default | Description |
|------|---------|-------------|
| `NoTracking` | `true` | Entities are not tracked (read-only queries are the common case) |
| `SplitQuery` | `false` | Use split queries for collection navigations to avoid cartesian explosion |
| `IgnoreGlobalFilters` | `false` | Bypass global query filters (soft-delete, tenant, etc.) |

### Include Paths

`IIncludableQuery<TEntity>` declares navigation paths for eager loading using dot notation:

```csharp
[Query<Order>]
public partial class GetOrderWithDetails : IIncludableQuery<Order>
{
    public IReadOnlyList<string> IncludePaths => ["Customer", "Lines.Product"];
    // ↑ Translates to: .Include(o => o.Customer).Include(o => o.Lines).ThenInclude(l => l.Product)
}
```

## `IQueryExecutor`

The runtime component that takes a query object and a source `IQueryable`, then executes it:

```csharp
public interface IQueryExecutor
{
    Task<PagedResult<TEntity>> ExecuteAsync<TEntity>(
        IPagedQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class;

    Task<PagedResult<TResult>> ExecuteAsync<TEntity, TResult>(
        IPagedQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class where TResult : class;

    Task<IReadOnlyList<TEntity>> ExecuteAllAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class;

    Task<IReadOnlyList<TResult>> ExecuteAllAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class where TResult : class;

    // At most one row: NotFoundError when there is none
    Task<Result<TEntity>> ExecuteSingleAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class;

    Task<Result<TResult>> ExecuteSingleAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class where TResult : class;
}
```

Four shapes, and the one to know is the last: `ExecuteSingleAsync` reads **First, not Single**. The
query owns the filter and the ordering, so a query that matches two rows returns the first rather than
throwing: "the most recent one" is a legitimate shape, and a query meant to match one and matching two
is a bug in the query.

The EF Core implementation (`EfCoreQueryExecutor`) automatically:

1. **Applies query hints**: `AsNoTracking()`, `AsSplitQuery()` based on `IQueryHints`
2. **Applies global filters**: soft-delete, tenant, temporal via `IQueryFilterProvider` (see [Query Filters](/modules/persistence/07-query-filters/))
3. **Applies navigation filters**: via `FilterMapComposer` + `PragmaticQueryFilterVisitor`
4. **Calls `Apply()`**: your generated filter/sort pipeline
5. **Handles projection**: applies the `Projection` expression via `Select()`
6. **Counts total items**: a separate `CountAsync()` for paging metadata
7. **Pages the results**: `Skip()` + `Take()` from `IPagedQuery`

The result is a `PagedResult<T>` that follows the Result pattern:

```csharp
var result = await executor.ExecuteAsync(query, source, ct);

// Pattern matching
return result.Match(
    success: (items, total) => Ok(new { items, total, result.Page, result.TotalPages }),
    failure: error => BadRequest(error.Message));

// Or direct access
if (result.IsSuccess)
{
    var items = result.Items;       // IReadOnlyList<T>
    var total = result.TotalCount;  // Total across all pages
    var pages = result.TotalPages;  // Calculated page count
    var hasNext = result.HasNextPage;
}
```

## `PagedResult<T>`

The paged result carries both the data and paging metadata:

| Property | Type | Description |
|----------|------|-------------|
| `Items` | `IReadOnlyList<T>` | Items for the current page |
| `TotalCount` | `int` | Total items across all pages |
| `Page` | `int` | Current page number (1-based) |
| `PageSize` | `int` | Items per page |
| `TotalPages` | `int` | Calculated: `ceil(TotalCount / PageSize)` |
| `HasPreviousPage` | `bool` | `Page > 1` |
| `HasNextPage` | `bool` | `Page < TotalPages` |

`PagedResult<T>` defaults to `QueryError` as its error type. For custom error types, use `PagedResult<T, TError>`.

## How It All Fits Together

> The route is not the only door. The same three steps run when another operation invokes the query
> in process, and the way to do that is the boundary it belongs to:
> `booking.Reservations.SearchReservations(status, page, pageSize, ct)`, with the overload that takes
> the query object beside it. That member goes through the invoker, so a caller in another module gets
> the validation and the permission the route gets.
>
> Before the invoker existed, both lived in the generated HTTP handler and nowhere else, and reading
> from code took three pieces (build the query, get a source, pick an executor overload), which is why
> no application did it.

Here is the full flow from HTTP request to response:

```
HTTP GET /api/reservations/search?Status=Confirmed&CheckInSort=Descending&Page=2

  1. Model binding → SearchReservationsQuery { Status = Confirmed, CheckInSort = Descending, Page = 2 }
  2. Endpoint pipeline invokes SearchReservationsQuery.Invoker
  2a. Invoker validates the input (the query's own ISyncValidator) → 400 if it refuses
  2b. Invoker checks the permission the query declares → 403 if the caller lacks it
  2c. Invoker hands the raw DbSet of the boundary's context to IQueryExecutor
  3. Executor gets IQueryable<Reservation> from that source
  4. Executor applies IQueryHints (AsNoTracking)
  5. Executor applies global query filters (soft-delete, tenant)
  6. Executor calls query.Apply(source) → adds WHERE + ORDER BY
  7. Executor counts total (SELECT COUNT)
  8. Executor pages (SKIP 20 TAKE 20)
  9. Executor projects (SELECT → ReservationSummaryDto)
  10. Returns PagedResult<ReservationSummaryDto>

HTTP 200 { items: [...], totalCount: 47, page: 2, pageSize: 20, totalPages: 3 }
```

You wrote 15 lines of C#. The source generator and runtime did the rest.
