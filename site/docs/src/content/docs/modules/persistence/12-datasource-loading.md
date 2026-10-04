---
title: "Query Strategy and Loading Profiles"
description: "> Control how the repository executes a query, from full entity tracking to raw projections."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/12-datasource-loading.md
sidebar:
  order: 13
---
> Control how the repository executes a query, from full entity tracking to raw projections.

## `[QueryStrategy]`: Loading Strategy

### The Problem

Different reads want different sources. A list that answers with DTOs has no use for the change tracker. An admin export has to see the rows the filters hide. Everything else wants what it gets by default: tracked, filtered.

A read that does not say so loads the way every other read loads, and the identity map grows for rows nobody will write.

### The Solution

```csharp
[QueryStrategy(Strategy = QueryStrategy.Projection)]
[Query<Order, OrderListDto>]
[Endpoint(HttpVerb.Get, "/orders")]
public partial class GetOrderList
{
    // Read untracked: the rows are never written back
}
```

### `QueryStrategy` Options

| Strategy | Tracking | Filters | Use Case |
|----------|----------|---------|----------|
| `Projection` | No | Applied | Read-only lists, DTOs, API responses |
| `Entity` | Yes | Applied | Reads whose rows are handed to business logic |
| `Filtered` | Yes | Applied | Same as `Entity`: the repository renders both branches identically |
| `Raw` | No | None | Admin dashboards, data exports, migrations |

**Default behavior**: without `[QueryStrategy]` a read is tracked and filtered, the same as `Entity` and `Filtered`. There is no inference from the operation type: nothing looks at whether the query projects, and declaring nothing is not the same as declaring `Projection`.

⚠️ **`Raw` drops every automatic filter**, tenant isolation and soft delete included. It belongs to an export or an admin panel, not to a route a tenant calls.

### Where it is read

The strategy is applied where the source of the read is built, so the two callers that build one are the two places it takes effect:

- the generated `GET` handler of a query that carries `[Endpoint]`;
- the read contract of a query that carries `[Published]`, which calls the repository's `Query(QueryStrategy)` overload instead of the parameterless `Query()`.

Calling the repository by hand, you pass the strategy yourself: `repository.Query(QueryStrategy.Raw)`.

**Nothing reads it on a mutation**, and there is no way it could: a write loads its row tracked (it has to, to save it), and the filters a write lifts are declared with `[FilterMode]` or `[WithoutFilter<T>]`, one at a time rather than all at once.

### When to Override

```csharp
// Admin endpoint that needs to see soft-deleted records
[QueryStrategy(Strategy = QueryStrategy.Raw)]
[Query<Order, OrderAdminDto>]
[Endpoint(HttpVerb.Get, "/admin/orders")]
public partial class GetAllOrdersAdmin { }
```

---

## `[LoadWith<T>]`: Loading Profiles

### The Problem

Different views of the same entity need different navigation loading. An order list needs just the order fields. An order detail needs `Customer` and `LineItems`. An order invoice needs `LineItems.Product`. Writing `Include()` chains everywhere is repetitive, easy to get wrong, and invisible to the source generator.

### The Solution

Declare what you need on the query class. The source generator reads the entity's `[Relation.*]` attributes and generates the include paths.

```csharp
[LoadWith<Order>(MaxDepth = 2, SplitQuery = true)]
[Query<Order>(Single = true)]
[Endpoint(HttpVerb.Get, "api/orders/{id}/full")]
public partial class GetOrderAggregate
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; set; }
}
```

**The query answers with the entity, and that is not incidental.** Includes only have something to do when the entity itself comes back: a query that projects to a DTO names the navigations it reaches inside the projection, and the database turns them into JOINs. On a projecting query the profile is generated and nothing applies it; see [Eager Loading](/modules/persistence/20-eager-loading/) for what fills a DTO's navigations instead.

### Where it is read

The profile publishes its paths twice, from one model:

- `IncludePaths`: the strings `IQueryExecutor` applies. An entity-shaped query carrying `[LoadWith<T>]` composes this into its own `IncludePaths`, beside any `[EagerLoad]` paths and the response DTO's `RequiredNavigations`. This is the generated caller.
- `ApplyIncludes()`: the typed `Include()` / `ThenInclude()` chain, for composing a queryable by hand, as in `db.Set<Order>().ApplyIncludes()`.

What enters the paths is **only** what the EF configuration mapped as a navigation, read with the same rule the entity configuration uses. A collection of scalars is a JSON column and a `[ValueObject]` is flattened into the owner's columns; `Include` over either is refused by EF Core before it reads a row, and no `MaxDepth` avoids them.

Nor does the path walk back. Past depth 1, the inverse navigation returning to the type the path arrived from is skipped: EF Core refuses an include that walks back up the include tree, and the fix-up populates it anyway. Only the immediate parent (a self-relation at the root, `Include(e => e.Parent)` on a tree node) is a legitimate first hop.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxDepth` | `int` | `1` | How deep to include navigations (0 = none) |
| `SplitQuery` | `bool` | auto | Split queries for collections. **Left unset it is decided for you**: two or more collection navigations turn it on |

`MaxDepth` means *every* navigation of the entity down to that depth: the ones you wrote and the ones
`[Relation.*]` declares alike. It is not "the ones the DTO reads": for that, and for a query that
projects, see [Eager Loading](/modules/persistence/20-eager-loading/), where a DTO's `RequiredNavigations` are derived
from its own shape.

### How Depth Works

Given an `Order` entity with navigations to `Customer`, `LineItems`, and `LineItems.Product`:

```
Depth 0: Order only (no navigations)
Depth 1: Order -> Customer, Order -> LineItems
Depth 2: Order -> Customer -> Address, Order -> LineItems -> Product
Depth 3: Order -> LineItems -> Product -> Category
```

The source generator reads all `[Relation.*]` attributes on the entity and its related types, then generates the include paths up to `MaxDepth`. A depth that reaches no navigation generates no profile at all, and the query then names none.

**Performance warning**: Depth > 3 emits diagnostic `PRAG0711`. Deep includes cause large SQL joins and cartesian explosion with collection navigations. If you need data from deeply nested types, consider using `QueryStrategy.Projection` to select only the fields you need.

### When to Use `SplitQuery`

EF Core joins all navigations into a single SQL query by default. With collection navigations, this causes cartesian explosion: every row in the parent is repeated for every row in each child collection.

```csharp
// Without SplitQuery: one big JOIN
// If Order has 3 LineItems and 2 Tags: 3 x 2 = 6 rows returned for one Order
[LoadWith<Order>(MaxDepth = 2)]

// With SplitQuery: separate SQL queries per collection
// Query 1: Orders + Customer (1:1 join, no duplication)
// Query 2: LineItems for matched Orders
// Query 3: Tags for matched Orders
[LoadWith<Order>(MaxDepth = 2, SplitQuery = true)]
```

**Rule of thumb**: Use `SplitQuery = true` when including more than one collection navigation at the same depth level.

---

## How QueryStrategy, LoadWith, and Queries Compose

The attributes control orthogonal concerns:

```
[QueryStrategy] -> Controls tracking + filter behavior
[LoadWith]      -> Controls eager loading (Include depth + split)
[Query]         -> Controls filtering + sorting + projection
```

They compose in a fixed order during query execution:

1. **Strategy**: `AsNoTracking()` applied (if Projection or Raw)
2. **Filters**: Query filters applied or skipped (based on strategy)
3. **Includes**: `Include()` / `ThenInclude()` calls generated from `[LoadWith]`
4. **Where**: Filter properties from the query class
5. **OrderBy**: Sort properties from the query class
6. **Projection**: `Select()` to the result DTO type
7. **Paging**: `Skip()` / `Take()` if pagination properties exist

### Full Example

```csharp
[QueryStrategy(Strategy = QueryStrategy.Projection)]
[Query<Order, OrderSummaryDto>]
[Endpoint(HttpVerb.Get, "/orders/summaries")]
public partial class GetOrderSummaries
{
    [Filter]
    public OrderStatus? Status { get; set; }

    [Sort(DefaultDirection = SortDirection.Descending)]     // applied even when the property is null
    public SortDirection? CreatedAt { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

The generated code:

1. `AsNoTracking()`: Projection strategy, no tracking overhead
2. `.Where(o => o.Status == status)`: only when `Status` is not null
3. `.OrderByDescending(o => o.CreatedAt)`: default descending
4. `.Select(o => new OrderSummaryDto { ... })`: projection
5. `.Skip((page - 1) * pageSize).Take(pageSize)`: pagination

No includes: this query projects. `[LoadWith]` on it would generate a profile that nothing applies; the projection is what reaches the navigations.

### Without Any Attributes

If you don't use `[QueryStrategy]` or `[LoadWith]`, the defaults apply:

```csharp
// Minimal query, tracked and filtered, and NO includes at all: [LoadWith] is a trigger,
// so without it no profile is generated. A projection resolves what it needs in SQL.
[Query<Order, OrderDto>]
public partial class GetOrders { }
```

Declaring nothing is a choice the generator honours literally: it emits the source it has always emitted. `[QueryStrategy]` is what says otherwise.
