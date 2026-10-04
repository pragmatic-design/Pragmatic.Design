---
title: "Filter Pipeline"
description: "Query filters (see Query Filters) handle **root-level** filtering: when you query `repo.Query()`, soft-deleted records are automatically excluded."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/efcore/06-filter-pipeline.md
sidebar:
  order: 27
---
## The Problem

Query filters (see [Query Filters](/modules/persistence/07-query-filters/)) handle **root-level** filtering: when you query `repo.Query()`, soft-deleted records are automatically excluded.

But what about **navigation properties**? Consider:

```csharp
var order = await repo.GetByIdAsync(orderId,
    q => q.Include(o => o.Items),
    ct);
```

If `LineItem` has `[SoftDelete]`, should `order.Items` include soft-deleted line items? No, but EF Core's `Include()` loads **all** related records. You'd need to write:

```csharp
q.Include(o => o.Items.Where(i => !i.IsDeleted))
```

...on every Include, for every soft-deletable navigation, everywhere in your code. Forget one, and you leak deleted data.

## The Solution: FilterMapComposer + Expression Visitor

The filter pipeline automatically injects `.Where()` clauses into `Include` and `ThenInclude` expressions. You write normal `Include()` calls, and the pipeline adds the filters.

```csharp
// What you write:
q.Include(o => o.Items)

// What the pipeline transforms it to:
q.Include(o => o.Items.Where(i => !i.IsDeleted))
```

## How It Works

### Step 1: FilterMapRegistry (Source-Generated)

The SG generates a static registry mapping entity types to navigation filter expressions:

```csharp
// ═══ Generated: _Infra.Persistence.FilterMap.g.cs ═══
public static class FilterMapRegistry
{
    // Enumerable.Where<T> resolved once, through an expression tree instead of GetMethods()
    private static readonly Dictionary<Type, MethodInfo> WhereMethods = new()
    {
        [typeof(LineItem)] = _where_LineItem,
        [typeof(UserRole)] = _where_UserRole,
    };

    // Stateless filters, built once and shared by every context
    private static readonly FilterMap SoftDeleteFilters = new(
        new Dictionary<Type, LambdaExpression>
        {
            [typeof(LineItem)] = (Expression<Func<LineItem, bool>>)(e => !e.IsDeleted),
        },
        WhereMethods);

    public static FilterMap CreateForContext(FilterContext context)
    {
        if (context.IsRaw)
            return FilterMap.Empty;          // Raw mode: the map is empty, not merely permissive

        var map = SoftDeleteFilters;
        // …context-dependent filters (tenant, temporal) composed on top
        return map;
    }
}
```

The registry is generated from your entity attributes: every `[SoftDelete]` entity contributes a
soft-delete entry, every `[TemporalRelation]` a temporal one. The `FilterMap` itself is **immutable**:
built from a dictionary in the constructor, with no `Add`, which is what lets the stateless part be
allocated once and reused across requests.

### Step 2: FilterMapComposer

Composes the final `FilterMap` from multiple sources:

```csharp
var composer = new FilterMapComposer(
    filterToggle,                               // For checking disabled filters
    visibilityProviders,                        // Custom runtime filters
    staticMapFactory: FilterMapRegistry.CreateForContext  // Generated registry
);
```

| Source | Origin | When Used |
|--------|--------|-----------|
| Static filters | `FilterMapRegistry` (generated) | Always: soft-delete, temporal |
| Visibility filters | `IVisibilityFilterProvider` (custom) | When you implement row-level security |
| Disabled filters | `IQueryFilterToggle` | Checked to skip disabled filters |

### Step 3: PragmaticQueryFilterVisitor

An **expression tree visitor** that walks the LINQ expression tree and injects `.Where()` into collection navigation expressions.

What it does:
1. Detects collection navigation expressions (`ICollection<T>`, `List<T>`)
2. Looks up a filter for the element type in the `FilterMap`
3. Wraps the navigation with `.Where(filter)`
4. Keeps the declared type where the expression needs it: an `Include`/`ThenInclude` is rebuilt as a
   filtered include, `Count` is read as `Enumerable.Count`, an `IEnumerable` argument takes the filter
   directly, and anything else gets `.ToList()`
5. Detects and skips already-filtered navigations (prevents double-filtering)

The executor runs it once over the final query, after a projection or an aggregate is composed, so a
collection read inside a `Select` is filtered like one read by `Include`.

### Step 4: Integration with Repository

The generated repository applies navigation filters automatically when you use `GetByIdAsync` with includes:

```csharp
// Inside the generated repository
public async Task<Order?> GetByIdAsync(
    Guid id,
    Func<IQueryable<Order>, IQueryable<Order>> includes,
    CancellationToken ct)
{
    var query = ApplyFilters(Set.AsQueryable());     // Root filters
    query = includes(query);                          // User's Include() calls
    query = ApplyNavigationFilters(query);            // ← Navigation filters injected here
    return await query.FirstOrDefaultAsync(e => e.PersistenceId == id, ct);
}
```

## EfCoreQueryExecutor

The `EfCoreQueryExecutor` provides the same filter integration for custom queries (paging, projections):

```csharp
var executor = new EfCoreQueryExecutor(
    filterProvider,       // IQueryFilterProvider? (root filters)
    filterMapComposer,    // FilterMapComposer?    (navigation filters)
    filterToggle,         // IQueryFilterToggle?   (disabled state)
    cacheStack,           // ICacheStack?          (caching for ICacheable queries)
    logger,               // ILogger?
    tenantContext,        // ITenantContext?       (also prefixes cache keys with "t:{tenant}:")
    currentUser,          // ICurrentUser?
    cacheStackResolver    // ICacheStackResolver?  (the stack named by ICacheable.CacheCategory)
);

var pagedResult = await executor.ExecuteAsync(myPagedQuery, db.Orders, ct);
// Returns: PagedResult<Order> with Items, TotalCount, Page, PageSize
```

### Features

| Feature | Description |
|---------|-------------|
| Root filters | Applied via `IQueryFilterProvider` |
| Navigation filters | Applied via `FilterMapComposer` |
| Paging | Returns `PagedResult<T>` with total count |
| Caching | Optional via `ICacheStack` for queries implementing `ICacheable` |
| Error mapping | EF Core exceptions → typed `QueryError` (Timeout, Connection, Database) |
| Query options | `AsNoTracking`, `AsSplitQuery`, `IgnoreQueryFilters` |

## Respecting IQueryFilterToggle

The entire pipeline respects `IQueryFilterToggle`:

```csharp
// Disable soft-delete filter: affects BOTH root queries AND navigation filtering
using (filterToggle.Disable<LineItem.SoftDeleteFilter>())
{
    var order = await repo.GetByIdAsync(orderId,
        q => q.Include(o => o.Items),
        ct);
    // order.Items includes soft-deleted line items
}
```

When a filter is disabled via `IQueryFilterToggle`, the `FilterMapRegistry` checks `context.DisabledFilters` and excludes it from the `FilterMap`. The visitor then has no filter to inject for that type.
