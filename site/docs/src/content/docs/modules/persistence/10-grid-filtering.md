---
title: "Grid Filtering"
description: "> Source-generated grid adapters for DevExpress, PrimeNG, and custom grid frameworks -- typed at compile"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/10-grid-filtering.md
sidebar:
  order: 11
---
> Source-generated grid adapters for DevExpress, PrimeNG, and custom grid frameworks -- typed at compile
> time, with the reflection confined to one guarded lookup.

## The Problem

Modern web applications use data grids (DevExpress, PrimeNG, AG Grid) that send filter/sort/page requests in their own proprietary JSON formats. Converting these to LINQ queries typically requires:

1. **Parsing the JSON format** -- different per framework, each with its own quirks
2. **Mapping field names to entity properties** -- string-based, no compile-time validation
3. **Building dynamic `Where`/`OrderBy` expressions** -- runtime reflection, expression trees by hand
4. **Handling operator logic** -- contains, equals, between, greaterThan, each with type coercion

This code is tedious, brittle, and repeated for every grid in the application. A typo in a field name compiles fine but fails at runtime. Adding a property to the entity does not warn you about outdated grid mappings.

## The Solution

Pragmatic offers two complementary approaches for grid scenarios, both source-generated:

| Approach | When to Use |
|----------|-------------|
| `[GridFilter<TEntity>]` | You own the filter DTO. The UI sends typed properties with dynamic operators. |
| `[GridAdapter<TEntity>]` | You consume an external grid framework's JSON format (DevExpress, PrimeNG). The SG generates a compile-time bridge from string field names to typed expressions. |

Both produce typed LINQ. The difference is where the dynamism lives -- in your DTO properties
(`GridFilter`) or in the external JSON field names (`GridAdapter`) -- and that difference decides how
much reflection is left:

| Path | Reflection at runtime |
|------|-----------------------|
| `[GridFilter<T>]` | **None.** Every property is declared, so the generated `Apply()` is a chain of typed comparisons |
| `[GenerateGridBridge]` | **None.** The bridge is a generated `switch` on field name, one typed branch per property |
| `DevExpressAdapter` / `PrimeNGAdapter` | **Yes**, one guarded lookup per field: the caller supplies the name, so `AdapterFieldPolicy` resolves it with `Type.GetProperty` and refuses anything that is not a non-sensitive scalar |

The runtime adapters are annotated (`DynamicallyAccessedMembers`) so the requirement reaches the caller
and trimming keeps what the lookup needs.

⚠️ **The two paths do not expose the same surface.** The bridge names only what the entity declares
with `[Filterable]`; the runtime adapters refuse a fixed list of sensitive names and resolve anything
else, and they do not read that declaration. Going through an adapter is therefore wider than going
through the bridge. Prefer the bridge where you can; where you cannot, the entity's declaration is not
yet what limits the adapter.

## `[GridFilter<TEntity>]` -- Typed Dynamic Filtering

A `GridFilter` is a class you write with explicit properties for each filterable/sortable field. Unlike `[Query<T, R>]` where each property has a fixed operator (e.g., "Name always uses Contains"), `GridFilter` supports **runtime operator selection** -- the UI tells the backend _how_ to compare, not just _what_ to compare.

### Defining a GridFilter

```csharp
[GridFilter<Order>]
public partial class OrderGridFilter
{
    // String filter with dynamic operator selection
    [Filterable(Operators = FilterOps.String)]
    public string? OrderNumber { get; set; }
    public StringOperator? OrderNumberOperator { get; set; }  // UI picks: Contains, StartsWith, etc.

    // Numeric range filter (Min/Max map to same entity property)
    [Filterable(Operators = FilterOps.Range, MapTo = "Total")]
    public decimal? MinTotal { get; set; }

    [Filterable(Operators = FilterOps.Range, MapTo = "Total")]
    public decimal? MaxTotal { get; set; }

    // Simple equality filter (enum/bool -- no operator needed)
    [Filterable]
    public OrderStatus? Status { get; set; }

    // Sort with default direction
    [Sort(DefaultDirection = SortDirection.Ascending, MapTo = "OrderNumber")]
    public SortDirection? OrderNumberSort { get; set; }

    [Sort(MapTo = "CreatedAt")]
    public SortDirection? CreatedAtSort { get; set; }

    // Pagination
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

### What the Source Generator Produces

The SG generates an `Apply()` method on the partial class that converts each non-null property into a typed LINQ expression:

```csharp
// Generated: OrderGridFilter.Apply.g.cs
public partial class OrderGridFilter
{
    public IQueryable<Order> Apply(IQueryable<Order> query)
    {
        // String filter with dynamic operator
        if (OrderNumber != null)
        {
            var op = OrderNumberOperator ?? StringOperator.Contains;
            query = op switch
            {
                StringOperator.Equals      => query.Where(e => e.OrderNumber == OrderNumber),
                StringOperator.Contains    => query.Where(e => e.OrderNumber.Contains(OrderNumber)),
                StringOperator.StartsWith  => query.Where(e => e.OrderNumber.StartsWith(OrderNumber)),
                StringOperator.EndsWith    => query.Where(e => e.OrderNumber.EndsWith(OrderNumber)),
                StringOperator.NotEquals   => query.Where(e => e.OrderNumber != OrderNumber),
                _ => query
            };
        }

        // Range filter
        if (MinTotal != null)
            query = query.Where(e => e.Total >= MinTotal);
        if (MaxTotal != null)
            query = query.Where(e => e.Total <= MaxTotal);

        // Equality filter
        if (Status != null)
            query = query.Where(e => e.Status == Status);

        // Sorting and paging...
        return query;
    }
}
```

All expressions are strongly typed. No `Expression.Property(parameter, "fieldName")`, no
`Convert.ChangeType`, no runtime reflection -- this is the `[GridFilter<T>]` path, where every field is
declared. The `DevExpressAdapter`/`PrimeNGAdapter` path resolves a caller-supplied name and does use
reflection; see the table above.

### Using GridFilter in an Endpoint

```csharp
[Endpoint(HttpVerb.Post, "/grid")]
[EndpointGroup<OrdersGroup>]
public partial class GridSearchOrdersEndpoint : Endpoint<PagedResult<OrderDto>>
{
    private IReadRepository<Order> _orders = null!;

    [FromBody]
    public required OrderGridFilter Filter { get; init; }

    public override async Task<Result<PagedResult<OrderDto>>> HandleAsync(CancellationToken ct = default)
    {
        var query = _orders.Query().AsNoTracking();
        query = Filter.Apply(query);  // Source-generated, type-safe

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Skip((Filter.Page - 1) * Filter.PageSize)
            .Take(Filter.PageSize)
            .Select(OrderDto.Projection!)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return PagedResult<OrderDto>.Success(items, totalCount, Filter.Page, Filter.PageSize);
    }
}
```

### `[Filterable]` Attribute

Marks a property as filterable. Controls which operators the UI can use.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Operators` | `FilterOps` | `FilterOps.All` | Which operators are allowed (flags enum) |
| `MapTo` | `string?` | `null` | Target entity property path (if different from property name) |
| `Handler` | `Type?` | `null` | ⚠️ **Read and discarded**: the handler is never called, and saying so is **PRAG0703**. Express the logic as a `[ComputedFilter]` on the entity |
| `HandlerArgs` | `string?` | `null` | ⚠️ Same: parsed, never used |

### `[SearchAcross]`: one box, several columns

On a `[GridFilter<T>]` property, `[SearchAcross]` expands one input into an OR across the named
columns:

```csharp
[SearchAcross("Name", "Code", "City")]
public string? Search { get; set; }
```

`IgnoreCase = true` lowers both sides; without it the comparison is the provider's, which on PostgreSQL
is case-sensitive. The same attribute on a `[Query<T, R>]` property searches the same way; see
[the query system](/modules/persistence/09-query-system/#searching-several-columns-searchacross).

### `FilterOps` Flags Enum

```csharp
[Flags]
public enum FilterOps
{
    None     = 0,
    Equality = 1,   // Equals, NotEquals
    String   = 2,   // Contains, StartsWith, EndsWith
    Compare  = 4,   // GreaterThan, GreaterOrEqual, LessThan, LessOrEqual
    Range    = 8,   // In, Between
    All      = Equality | String | Compare | Range
}
```

Use `FilterOps` to restrict what the UI can do:

```csharp
[Filterable(Operators = FilterOps.String)]          // Only string operations
[Filterable(Operators = FilterOps.Equality)]        // Only equals/not-equals
[Filterable(Operators = FilterOps.Compare | FilterOps.Range)]  // Numeric comparisons + range
[Filterable]                                         // All operators (default)
```

### `StringOperator` Enum

For string properties, a companion `{Name}Operator` property of type `StringOperator?` allows the UI to select the comparison mode at runtime:

```csharp
public enum StringOperator
{
    Equals,       // property == value
    Contains,     // property.Contains(value) -- default
    StartsWith,   // property.StartsWith(value)
    EndsWith,     // property.EndsWith(value)
    NotEquals     // property != value
}
```

If the operator property is `null`, the SG uses `Contains` as the default for string filters.

### `[Sort]` Attribute

Marks a property as a sort option. Works in both `[Query]` and `[GridFilter]` contexts.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MapTo` | `string?` | `null` | Target entity property (derives from property name minus "Sort" suffix if not set) |
| `DefaultDirection` | `SortDirection?` | unset | Default sort direction: `SortDirection.Ascending` / `SortDirection.Descending`; unset means no default |
| `Priority` | `int` | `0` | Sort priority when multiple sorts are active (lower = applied first) |

```csharp
// Dynamic sort -- user chooses direction
[Sort]
public SortDirection? NameSort { get; set; }

// Fixed default sort -- always applied as secondary sort
[Sort(Priority = 1, DefaultDirection = SortDirection.Descending)]  // secondary sort
public SortDirection? CreatedAtSort { get; set; }
```

## `[GridAdapter<TEntity>]` -- External Grid Framework Bridge

When you consume an external grid framework (DevExpress, PrimeNG, AG Grid), the frontend sends filter/sort/page requests in the framework's proprietary JSON format with **string field names**. A `GridAdapter` generates a compile-time mapping from those string names to entity properties.

### Defining a GridAdapter

```csharp
[GridAdapter<Order>(Framework = GridFramework.Both)]
[GridField("orderNo", Property = "OrderNumber")]
[GridField("customer.name", Property = "CustomerName")]
[GridExclude("InternalNotes")]
public partial class OrderGridAdapter { }
```

| Attribute Property | Type | Default | Description |
|--------------------|------|---------|-------------|
| `Framework` | `GridFramework` | `Both` | Which adapters to generate: `DevExpress`, `PrimeNG`, or `Both` |
| `SupportNestedFilters` | `bool` | `true` | Generate nested AND/OR filter tree support |
| `SupportGrouping` | `bool` | `true` | Generate grouping support |

### `[GridField]` -- Custom Field Mapping

Maps a JSON field name (sent by the frontend) to an entity property (used in LINQ).

```csharp
[GridField("orderNo", Property = "OrderNumber")]
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `JsonField` | `string` | (required) | The field name in the JSON payload from the grid UI |
| `Property` | `string?` | PascalCase of JsonField | The entity property path to map to |
| `Filterable` | `bool` | `true` | Whether this field can be filtered |
| `Sortable` | `bool` | `true` | Whether this field can be sorted |
| `Groupable` | `bool` | `true` | Whether this field can be grouped |
| `AllowedOperators` | `string[]?` | `null` | Restrict which operators are allowed for this field |

### `[GridExclude]` -- Hide Properties from the Grid

Prevents an entity property from being filterable, sortable, or groupable through the grid adapter:

```csharp
[GridExclude("InternalNotes")]   // Not exposed to the grid UI
[GridExclude("PasswordHash")]    // Security: never filter/sort by sensitive fields
```

## Grid Adapter Pipeline

The pipeline has three steps. Each step is decoupled, and the canonical format in the middle allows mixing and matching adapters and bridges.

```
External Grid JSON ──> Adapter ──> GridFilterRequest ──> SG Bridge ──> IQueryable<T>
     (runtime)          (runtime)     (canonical)       (compile-time)    (LINQ)
```

### Step 1: Grid Sends JSON

Each grid framework has its own request format:

**DevExpress** sends `LoadOptions` with nested filter arrays:
```json
{
  "filter": [["name", "contains", "Rome"], "and", ["starRating", ">=", 3]],
  "sort": [{ "selector": "name", "desc": false }],
  "skip": 0,
  "take": 20
}
```

**PrimeNG** sends `LazyLoadEvent` with field-based filter metadata:
```json
{
  "first": 0,
  "rows": 20,
  "sortField": "name",
  "sortOrder": 1,
  "filters": {
    "name": { "value": "Rome", "matchMode": "contains" },
    "starRating": { "value": 3, "matchMode": "gte" }
  }
}
```

### Step 2: Adapter Converts to `GridFilterRequest`

The canonical format is framework-agnostic. All external formats are converted into it:

```csharp
public record GridFilterRequest
{
    public IReadOnlyList<FilterClause> Filters { get; init; } = [];
    public IReadOnlyList<SortClause> Sorts { get; init; } = [];
    public IReadOnlyList<GroupClause> Groups { get; init; } = [];
    public int? Page { get; init; }    // 1-based, null = no paging
    public int? PageSize { get; init; }
}

public record FilterClause(string Field, FilterOperator Operator, object? Value, FilterLogic Logic = FilterLogic.And);
public record SortClause(string Field, SortDirection Direction);
public record GroupClause(string Field, SortDirection? SortDirection = null);
```

`FilterOperator` covers all common comparison types:

| Operator | Description |
|----------|-------------|
| `Equals` | `property == value` |
| `NotEquals` | `property != value` |
| `Contains` | `property.Contains(value)` -- default for strings |
| `StartsWith` | `property.StartsWith(value)` |
| `EndsWith` | `property.EndsWith(value)` |
| `GreaterThan` | `property > value` |
| `GreaterOrEqual` | `property >= value` |
| `LessThan` | `property < value` |
| `LessOrEqual` | `property <= value` |
| `In` | ⚠️ Not generated on the grid path; see below |
| `Between` | ⚠️ Not generated anywhere; see below |

⚠️ **A clause the generated code does not handle is dropped, not refused.** The `switch` ends in
`_ => query`, so an `In` or `Between` clause arriving from a grid returns the query **unrestricted**:
more rows than the caller asked for, with no error. What each path really covers:

| Path | Operators generated |
|---|---|
| `[Query]` / `[FilterDto]` | everything in the table except `Between`, and `Between` there degrades to `==` rather than being dropped |
| `[GridFilter<T>]`, `[GenerateGridBridge]` | `Equals`, `NotEquals` on any type; `Contains`, `StartsWith`, `EndsWith` on strings; the four comparisons on comparable types. Not `In`, not `Between` |

`FilterLogic` controls how clauses combine: `And` (all must match) or `Or` (any must match).

### Step 3: SG Bridge Converts to LINQ

Two different attributes produce the two halves of this step, and they are worth keeping apart.

**`[GridAdapter<TEntity>]`** generates the framework entry points as static methods **on the adapter
class itself**, one per framework asked for by `Framework`:

```csharp
// Generated: {Namespace}.OrderGridAdapter.GridAdapter.g.cs
public partial class OrderGridAdapter
{
    public static IQueryable<Order> Apply(IQueryable<Order> query, DevExpressLoadOptions options);
    public static IQueryable<Order> Apply(IQueryable<Order> query, PrimeNGLazyLoadEvent lazyEvent);
}
```

**`[GenerateGridBridge]`**, on the **entity**, generates the canonical bridge: the typed `switch` on
field name, with no reflection at all. It names **only the properties that declare `[Filterable]`**,
because the field name comes from the client: a list of what is forbidden covers whatever somebody
remembered to put in it, and sorting or filtering on a column makes it talk without reading it.
`[GridExclude("Name")]` on the entity takes back one of the declared ones; a property that declares
nothing was never in the bridge. The framework's reserved columns (credentials, `OwnerId`,
`TenantId`, `AccessScopes`, `PersistenceId`, `RowVersion`) are withheld **even when declared**.

```csharp
[Entity]
[GenerateGridBridge]
[GridExclude("SourceRef")]              // declared below, taken back here
public partial class Order
{
    [Filterable]
    public string OrderNumber { get; private set; } = "";

    [Filterable]
    public decimal Total { get; private set; }

    public string SourceRef { get; private set; } = "";   // declares nothing: not nameable
    public string InternalNotes { get; private set; } = ""; // idem
}

// Generated: {Namespace}.OrderGridFilterBridge.GridBridge.g.cs
public static class OrderGridFilterBridge
{
    public static IQueryable<Order> ApplyCanonical(
        this IQueryable<Order> query, GridFilterRequest request);
    // one typed branch per property; an unknown or withheld field is refused by name
}
```

The two compose: an adapter turns the framework's JSON into a `GridFilterRequest`, the bridge turns
that into LINQ. Neither needs the other: the bridge alone is enough for a UI that already speaks the
canonical format.

### The canonical request as a declared read

A `[Query]` can take the request as an input, and the generated `Apply` feeds it to the bridge. This
is the declarative half: the route, the permission, the published contract and the processing-register
entry all come from the declaration, where an action that fetches `Query()` and applies the request
itself declares none of them.

```csharp
[Query<Order, OrderDto>]
[RequirePermission(SalesPermissions.Order.Read)]
[Endpoint(HttpVerb.Post, "api/orders/grid")]      // POST: a request object needs a body
public partial class SearchOrdersGridQuery
{
    public GridFilterRequest? Grid { get; init; }  // recognised by type, not by an attribute
}

// Generated in SearchOrdersGridQuery.Query.g.cs
public IQueryable<Order> Apply(IQueryable<Order> query)
{
    if (this.Grid is not null)
        query = OrderGridFilterBridge.ApplyCanonical(query, this.Grid);

    return query;
}
```

Three things the compiler will tell you about:

- the entity must carry `[GenerateGridBridge]`, or there is no bridge to call (`PRAG0723`);
- the request carries its own `Page`/`PageSize` and the bridge applies them, so a query that also
  declares paging pages twice (`PRAG0724`);
- a request object cannot travel in a query string, so the route needs a verb with a body; on `GET`
  it is `PRAG0532`.

The fields the request may name are the bridge's, unchanged: `[Filterable]` and nothing else. Going
through a query does not widen by one column what the wire can reach.

## Built-In Adapters

Pragmatic ships two runtime adapters for the most common grid frameworks. These are extension methods on `QueryBuilder<T>` and work via runtime expression trees (not SG) because the external format uses string field names that cannot be validated at compile time.

### DevExpress Adapter

Handles DevExpress `DataSourceLoadOptions`: nested filter arrays (`["!", ["field", "op", value]]`), composite AND/OR, all comparison and string operators, value coercion from `JsonElement`.

```csharp
var builder = new QueryBuilder<Order>()
    .WithFilter(OrderSpecifications.Active)
    .FromDevExpress(loadOptions)
    .WithPaging(page, pageSize);

var query = builder.Build(repository.Query(), filterProvider);
```

⚠️ `QueryBuilder.Build()` applies **filters, sorting and paging, and nothing else**. `AsNoTracking()`
and `AsSplitQuery()` set a flag that no code reads: call them and the query still tracks. Ask the
repository for an untracked source instead (`repository.Query(QueryStrategy.Projection)`) or call
`.AsNoTracking()` on the `IQueryable` that comes out of `Build`. Pass the `IQueryFilterProvider` too:
without it `Build` applies no global filter at all.

**DevExpress filter format**: `["field", "operator", value]` arrays with `"and"`/`"or"` connectives. Supports negation via `["!", [subfilter]]`.

```json
[["name", "contains", "Rome"], "and", ["starRating", ">=", 3]]
```

### PrimeNG Adapter

Handles PrimeNG `LazyLoadEvent`: field-based filters with match modes, global filter across multiple fields, single and multi-column sorting, offset-based paging.

```csharp
var builder = new QueryBuilder<Order>()
    .FromPrimeNG(lazyLoadEvent);

var query = builder.Build(repository.Query(), filterProvider).AsNoTracking();
```

**PrimeNG match modes**: `equals`, `notEquals`, `contains`, `notContains`, `startsWith`, `endsWith`, `lt`, `lte`, `gt`, `gte`, `in`, `between`.

**Global filter**: Applies a `Contains` search across all specified `GlobalFilterFields` (string properties only), combined with OR logic.

### Custom Adapters

For other grid frameworks (AG Grid, Kendo, Syncfusion), implement `IGridFilterAdapter<TExternalFormat>`:

```csharp
public class AgGridAdapter : IGridFilterAdapter<AgGridRequest>
{
    public GridFilterRequest Adapt(AgGridRequest input)
    {
        return new GridFilterRequest
        {
            Filters = input.FilterModel
                .Select(f => new FilterClause(f.Field, MapOperator(f.Type), f.Filter))
                .ToList(),
            Sorts = input.SortModel
                .Select(s => new SortClause(s.ColId, s.Sort == "asc" ? SortDirection.Ascending : SortDirection.Descending))
                .ToList(),
            Page = input.StartRow / input.PageSize + 1,
            PageSize = input.PageSize
        };
    }
}
```

The adapter is a pure mapping function -- no DI state, no SG involvement. Once you have a `GridFilterRequest`, use it with `ApplyCanonical()` (SG bridge) or convert manually.

## `[GridFilter]` vs `[GridAdapter]` -- When They Combine

You can use both on the same entity. The `GridFilter` is for your own typed API. The `GridAdapter` is for consuming an external grid framework's native format. They share the same entity and produce complementary LINQ pipelines.

| Scenario | Type | Approach |
|----------|------|----------|
| Your API receives typed filter properties | Typed | `[GridFilter<Order>]` with `[Filterable]` properties |
| Your API receives DevExpress/PrimeNG JSON | Dynamic | `QueryBuilder.FromDevExpress(options)` or `FromPrimeNG(event)` |
| Your API receives a canonical `GridFilterRequest` | Canonical | `[GenerateGridBridge]` on the entity + `ApplyCanonical(request)` |
| Custom grid framework | Custom | `IGridFilterAdapter<T>` + canonical pipeline |

## When to Use What

Pragmatic offers multiple filtering mechanisms. Here is how to choose:

| Scenario | Attribute | Why |
|----------|-----------|-----|
| Fixed filters, known at compile time | `[Query<T, R>]` with `[Filter]` | Simplest. Each property has one fixed operator. Best for REST query parameters. |
| Reusable filter logic across endpoints | `[FilterDto<T>]` | Shared filter DTO, applied via `.Apply()`. Operators fixed at compile time. |
| Domain-specific filter groups | `[ComplexFilter]` on a property whose type is a `[FilterDto<T>]` | One query parameter carrying a JSON object, with explicit domain semantics. The attribute is not generic. |
| UI grid with dynamic operator selection | `[GridFilter<T>]` | The UI picks the operator at runtime (Contains, StartsWith, etc.). Typed, SG-generated. |
| DevExpress/PrimeNG native format | Built-in adapters | `FromDevExpress(options)` / `FromPrimeNG(event)` on `QueryBuilder`. Runtime expression trees. |
| Custom grid + compile-time bridge | `[GenerateGridBridge]` on the entity | Maps string field names to typed expressions via SG, with no reflection. Feed it a `GridFilterRequest` from your own adapter. |

## Showcase Example

The Showcase project demonstrates all three approaches for the `Property` entity in the Catalog module:

```csharp
// 1. [Query] -- fixed filters, REST parameters
// GET /api/v1/properties?name=Rome&minStarRating=3
[Query<Property, PropertySummaryDto>]
public partial class SearchPropertiesQuery { ... }

// 2. [GridFilter] -- typed DTO with dynamic operators
// POST /api/v1/properties/grid
[GridFilter<Property>]
public partial class PropertyGridFilter
{
    [Filterable(Operators = FilterOps.String)]
    public string? Name { get; set; }
    public StringOperator? NameOperator { get; set; }

    [Filterable(Operators = FilterOps.Range, MapTo = "StarRating")]
    public int? MinStarRating { get; set; }

    [Filterable(Operators = FilterOps.Range, MapTo = "StarRating")]
    public int? MaxStarRating { get; set; }

    [Sort(DefaultDirection = SortDirection.Ascending, MapTo = "Name")]
    public SortDirection? NameSort { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// 3. DevExpress adapter -- external format
// POST /api/v1/properties/devexpress
var builder = new QueryBuilder<Property>()
    .WithFilter(PropertySpecifications.Active)
    .FromDevExpress(loadOptions);
```

## Design Rationale

**Why two approaches instead of one?**

`GridFilter` and `GridAdapter` solve different problems. A `GridFilter` is your own API contract -- you control the shape, the types, and the allowed operators. A `GridAdapter` consumes someone else's format and maps it to your entities. The canonical `GridFilterRequest` sits in the middle, allowing adapters to feed into SG bridges.

**Why source-generated instead of runtime expression builders?**

Runtime expression builders (like the built-in DevExpress/PrimeNG adapters) use `Expression.Property(parameter, fieldName)` which resolves properties by string name at runtime. This works but has three drawbacks: (1) no compile-time validation of field names, (2) runtime reflection overhead, (3) no opportunity for the compiler to catch type mismatches. The SG approach (`GridFilter.Apply()`, `{Entity}GridFilterBridge.ApplyCanonical()`) produces typed LINQ that is validated at compile time and has zero reflection overhead.

**Why keep runtime adapters alongside SG?**

The built-in DevExpress and PrimeNG adapters use runtime expression trees because the external format is inherently dynamic -- the field names come from JSON at runtime. The SG bridge (`GridAdapter + ApplyCanonical`) pre-validates the field mapping at compile time, but the initial JSON parsing is still runtime. The runtime adapters are simpler for quick integration; the SG bridge is better for production systems where you want compile-time guarantees on every field mapping.
