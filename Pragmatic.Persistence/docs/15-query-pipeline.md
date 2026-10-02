# The Query Pipeline

> From HTTP request to SQL and back — every step, every decision point, every extension hook.

This document explains how a query flows through the Pragmatic stack. Understanding the pipeline helps you make informed decisions about performance, caching, filtering, and projection.

---

## Pipeline Overview

```
HTTP GET /api/reservations/search?Status=Confirmed&Page=2
    │
    ▼
┌─────────────────────────────────────────────────┐
│  1. Endpoint Handler (source-generated)         │
│     • 401 if unauthenticated, 403 by policy      │
│     • Generated binding, per parameter           │
│       — a malformed value is a 400 naming it     │
│     • Claim binding from HttpContext.User        │
│     • IResourceAuthorizer<TQuery> → 403          │
│     • ISyncValidator on the query                │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  1b. The query's Invoker (source-generated)     │
│     • Validation, then the permission           │
│     • [FromCurrentUser] properties filled from  │
│       the caller — 401 / 404 when it cannot     │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  2. IQueryExecutor.ExecuteAsync()               │
│     • Resolve from DI                           │
│     • Receives: query + IQueryable<TEntity>     │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  3. Prepare Source                              │
│     • AsNoTracking() (if IQueryHints.NoTracking)│
│     • AsSplitQuery() (if IQueryHints.SplitQuery)│
│     • IgnoreQueryFilters() (if requested)       │
│     • Include() paths (from IIncludableQuery)   │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  4. Apply Global Filters                        │
│     a. Build FilterContext (mode, tenant, user)  │
│     b. IQueryFilterProvider.GetCombinedFilter()  │
│        → Soft-delete, tenant, permission filters │
│     c. FilterMapComposer.ApplyNavigationFilters()│
│        → PragmaticQueryFilterVisitor walks       │
│          Include expressions, injects .Where()   │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  5. query.Apply(source)                         │
│     • YOUR generated filters (WHERE clauses)    │
│     • YOUR generated sorts (ORDER BY)           │
│     • This is the SG-generated Apply() method   │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  6. Count + Page + Project                      │
│     a. SELECT COUNT(*) (total items)            │
│     b. Skip() + Take() (pagination)            │
│     c. Select(Projection) (DTO mapping)         │
│     d. ToListAsync() (materialize)              │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  7. Return PagedResult<TResult>                 │
│     • Items, TotalCount, Page, TotalPages       │
│     • Or QueryError on failure                  │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
HTTP 200 { items: [...], totalCount: 47, page: 2, totalPages: 3 }
```

---

## Step 1: Endpoint Handler

When a query class has both `[Query<T, R>]` and `[Endpoint]`, the source generator produces a Minimal API handler.

```csharp
// ═══ What YOU write ═══
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

The generated file is `{Namespace}.SearchReservationsQuery.Endpoint.g.cs`, and what it adds to the
query's partial class is a `MapEndpoint` the host calls:

```csharp
// ═══ What the SG generates (shape, not verbatim) ═══
public partial class SearchReservationsQuery
{
    private static readonly ResourcePolicy __policy = new ReservationSearchPolicy();

    public static IEndpointConventionBuilder MapEndpoint(IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/api/reservations/search", (RequestDelegate)(async httpContext =>
        {
            // 1. authentication — before anything is read
            var user = httpContext.RequestServices.GetService<ICurrentUser>();
            if (user is null || !user.IsAuthenticated) { httpContext.Response.StatusCode = 401; return; }

            // 2. the endpoint's policy
            if (!__policy.Evaluate(user)) { httpContext.Response.StatusCode = 403; return; }

            // 3. services, from the request scope — the DbContext keyed on the boundary
            var executor  = httpContext.RequestServices.GetRequiredService<IQueryExecutor>();
            var dbContext = httpContext.RequestServices
                .GetRequiredKeyedService<DbContext>(typeof(BookingBoundary));

            // 4. binding, written out per parameter
            var raw = RequestValues.Query(httpContext, "guestId");
            Guid? guestId = null;
            if (!string.IsNullOrEmpty(raw))
            {
                if (!RequestBinder.TryBind<Guid>(raw, out var parsed))
                {
                    await BindingFailure.WriteAsync(httpContext, "guestId", "the value is malformed");
                    return;                                  // 400, naming the parameter
                }
                guestId = parsed;
            }
            // … one block per filter, sort, paging and claim property

            var query = new SearchReservationsQuery { GuestId = guestId, /* … */ };

            // 5. per-instance authorization, when an IResourceAuthorizer<TQuery> is registered
            // 6. validation, when the query implements ISyncValidator
            // 7. execute
            var result = await executor.ExecuteAsync<Reservation, ReservationSummaryDto>(
                query, dbContext.Set<Reservation>(), ct);
        }));
}
```

### Key Points

- **It is a `RequestDelegate`, not a minimal-API handler with parameters.** ASP.NET's own binding never
  runs: the generator writes the binding, and that is what makes the endpoint AOT-safe. The practical
  consequence is that endpoint **filters see no bound arguments** — there are none to see.
- **A malformed value is a 400 that names the parameter**, from `BindingFailure.WriteAsync`, before the
  query object exists.
- **DbContext is keyed by boundary** — `[BelongsTo<BookingBoundary>]` on the entity decides the key, and
  it is resolved from the request scope rather than injected.
- **Four gates, in this order**: authentication (401), the endpoint's `ResourcePolicy` (403),
  `IResourceAuthorizer<TQuery>` when one is registered (403), then validation.
- **Claim binding** — properties marked `[FromClaim("sub")]` are bound from `HttpContext.User`, in the
  same generated block as the query parameters.
- **A `[FromCurrentUser]` property is not bound here at all** — not from the query string, not from the
  route. The invoker fills it (next step).

---

## Step 1b: The Query Invoker

Every declared query gets a nested `Invoker` (`{Namespace}.{Query}.QueryInvoker.g.cs`), and it is the one
way to run it: the generated handler calls it, and so does the query's member on the boundary interface,
in process. It derives from `QueryInvoker<TQuery>`, which runs, in order:

1. **validation** — the query's `ISyncValidator`, when it has one;
2. **the permission** — what `[RequirePermission]` declares, written into the invoker as a literal;
3. **the read** — a lambda the generator writes, which starts by filling the `[FromCurrentUser]`
   properties and then hands the query to `IQueryExecutor`.

```csharp
// ═══ What the SG generates for GetMyBalancesQuery (shape) ═══
=> RunAsync(query, async q =>
{
    var currentUser = /* ICurrentUser, or AnonymousUser when none is registered */;
    if (!currentUser.IsAuthenticated)
        return Result<…>.Failure(UnauthorizedError.Create());                 // 401

    // Member form: the [PragmaticUser] entity, through the resolver this generator writes
    var user = await new EmployeeResolver(
            services.GetRequiredService<IReadRepository<Employee>>(), currentUser)
        .ResolveAsync(cancellationToken);
    if (user is null)
        return Result<…>.Failure(NotFoundError.For("Employee", currentUser.Id)); // 404

    q.EmployeeId = user.Id;           // the private setter: only the nested Invoker reaches it

    var executor = services.GetRequiredService<IQueryExecutor>();
    …                                  // the read: steps 2–7
}, cancellationToken);
```

- **After validation and the permission, before the read.** A refused caller is refused before anyone is
  resolved, and the value is on the query when the executor builds the cache key and applies `Apply()`.
- **The member-less form** writes `q.OwnerId = currentUser.Id;` and resolves nothing.
- **The resolver is constructed, not resolved from DI.** Nothing depends on it being registered.

Declaring the binding, its two forms, and `PRAG0730`/`PRAG0731`:
[Filtering by the caller](09-query-system.md#filtering-by-the-caller--fromcurrentuser).

---

## Step 2: IQueryExecutor

The runtime component that orchestrates execution. It is **not** query-specific — one instance handles all queries.

```csharp
public interface IQueryExecutor
{
    // Paged query with projection
    Task<PagedResult<TResult>> ExecuteAsync<TEntity, TResult>(
        IPagedQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken ct = default)
        where TEntity : class where TResult : class;

    // Paged query without projection (returns entities)
    Task<PagedResult<TEntity>> ExecuteAsync<TEntity>(
        IPagedQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken ct = default) where TEntity : class;

    // Non-paged query (returns all matching items), with and without projection
    Task<IReadOnlyList<TEntity>> ExecuteAllAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken ct = default) where TEntity : class;

    Task<IReadOnlyList<TResult>> ExecuteAllAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken ct = default) where TEntity : class where TResult : class;

    // At most one row — NotFoundError when there is none. First, not Single.
    Task<Result<TEntity>> ExecuteSingleAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken ct = default) where TEntity : class;

    Task<Result<TResult>> ExecuteSingleAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken ct = default) where TEntity : class where TResult : class;
}
```

### EfCoreQueryExecutor

The EF Core implementation. It adds:

| Feature | How |
|---------|-----|
| Root filters | `IQueryFilterProvider.GetCombinedFilter<T>(context)` |
| Navigation filters | `FilterMapComposer` + `PragmaticQueryFilterVisitor` |
| Query hints | `AsNoTracking()`, `AsSplitQuery()`, `IgnoreQueryFilters()` |
| Caching | `ICacheStack` for queries implementing `ICacheable` |
| Error mapping | EF Core exceptions → typed `QueryError` |

Constructor — you rarely instantiate this yourself; it is registered by the generated DI code:

```csharp
var executor = new EfCoreQueryExecutor(
    filterProvider,       // IQueryFilterProvider? — root filters
    filterMapComposer,    // FilterMapComposer?    — navigation filters
    filterToggle,         // IQueryFilterToggle?   — disabled state
    cacheStack,           // ICacheStack?          — caching for ICacheable queries
    logger,               // ILogger?
    tenantContext,        // ITenantContext?       — also prefixes every cache key with "t:{tenant}:"
    currentUser,          // ICurrentUser?
    cacheStackResolver    // ICacheStackResolver?  — picks the stack named by ICacheable.CacheCategory
);
```

Every parameter is optional, and each one that is absent removes a capability quietly: no
`filterProvider` means no root filters, no `cacheStack` means no caching, no `tenantContext` means one
cache shared across tenants.

---

## Step 3: Prepare Source

Before applying your query's filters, the executor prepares the `IQueryable`:

```csharp
// Internal: PrepareSource()
private IQueryable<TEntity> PrepareSource<TEntity>(
    IQueryable<TEntity> source, IQuery<TEntity> query)
{
    // 1. Query hints
    if (query is IQueryHints hints)
    {
        if (hints.NoTracking)
            source = source.AsNoTracking();
        if (hints.SplitQuery)
            source = source.AsSplitQuery();
        if (hints.IgnoreGlobalFilters)
            source = source.IgnoreQueryFilters();
    }
    else
    {
        source = source.AsNoTracking();   // no hints declared → untracked all the same
    }

    // 2. Include paths
    if (query is IIncludableQuery<TEntity> includable)
    {
        foreach (var path in includable.IncludePaths)
            source = source.Include(path);
    }

    return source;
}
```

### IQueryHints Defaults

| Hint | Default | Effect |
|------|---------|--------|
| `NoTracking` | `true` | Entities are not tracked — best for read-only queries |
| `SplitQuery` | `false` | Single SQL query — set to `true` with multiple collection navigations |
| `IgnoreGlobalFilters` | `false` | EF Core global query filters apply — set to `true` for admin views |

Most queries do not implement `IQueryHints` at all, and the `else` branch above is why that is safe:
a query with no hints is untracked, exactly as if it had declared the default. Implement the interface
when you need the opposite — tracked entities, or a split query.

⚠️ `IgnoreGlobalFilters` calls EF Core's `IgnoreQueryFilters()`, which removes **EF's own** named
filters. The Pragmatic filters are a `Where` composed on top and are not affected by it: to drop those,
use `IQueryFilterToggle` or `FilterMode.Raw`.

---

## Step 4: Apply Global Filters

This is the most complex step. It has two phases: **root filtering** and **navigation filtering**.

### Phase 4a: Build FilterContext

```csharp
var context = new FilterContext
{
    TenantId = tenantContext?.TenantId,
    UserId = currentUser?.Id,
    Now = timeProvider.GetUtcNow(),
    DisabledFilters = filterToggle?.GetDisabledFilters() ?? new HashSet<Type>(),
    Mode = filterToggle?.CurrentMode ?? FilterMode.Normal
};
```

The `FilterContext` carries all the runtime information filters need to decide what to show.

### Phase 4b: Root Filters

`IQueryFilterProvider.GetCombinedFilter<TEntity>(context)` combines all registered filters into a single `Expression<Func<TEntity, bool>>`:

```csharp
// Conceptually (simplified):
// 1. Soft-delete filter: e => !e.IsDeleted (if [SoftDelete])
// 2. Tenant filter: e => e.TenantId == context.TenantId (if tenant-aware)
// 3. Permission filter: e => e.TeamId == context.TeamId (if IPermissionBasedFilter)
// Combined: e => !e.IsDeleted && e.TenantId == "T1" && e.TeamId == "team-42"
```

Which filters actually apply depends on `FilterMode`:

| Mode | Soft-delete | Tenant | Data scope | Visibility | Permission |
|------|---|---|---|---|---|
| **Normal** (0) | Yes | Yes | Yes | Yes | Yes |
| **Admin** (1) | Yes | Yes | Yes | No | No |
| **Elevated** (2) | Yes | Yes | No | No | No |
| **Background** (3) | Yes | No | No | No | No |
| **Raw** (4) | No | No | No | No | No |

The modes are ordered and cumulative — `FilterContext` reads them as `Mode >= Admin`, `Mode >= Elevated`,
`Mode >= Background`. `Raw` is the only one that reaches soft-delete, and it does so by leaving the
filter map empty rather than by a flag of its own.

### Phase 4c: Navigation Filters

After root filters, `FilterMapComposer` builds a `FilterMap` — a dictionary of `Type → Expression`. Then `PragmaticQueryFilterVisitor` walks the expression tree and injects `.Where()` into collection navigations:

```csharp
// Before visitor:
query.Include(o => o.Items)

// After visitor (if LineItem has [SoftDelete]):
query.Include(o => o.Items.Where(i => !i.IsDeleted))
```

The visitor is smart:
- **Detects already-filtered navigations** — if you wrote `Include(o => o.Items.Where(...))`, the visitor won't double-filter.
- **Handles ThenInclude chains** — `Include(o => o.Items).ThenInclude(i => i.Tags)` — filters both `Items` and `Tags` if both have registered filters.
- **Respects IQueryFilterToggle** — if you disabled `LineItem.SoftDeleteFilter`, the visitor skips it.

---

## Step 5: query.Apply()

This is YOUR code — the source-generated `Apply()` method. It adds `WHERE` and `ORDER BY` clauses based on the query's `[Filter]` and `[Sort]` properties.

```csharp
// ═══ Generated for SearchReservationsQuery ═══
public IQueryable<Reservation> Apply(IQueryable<Reservation> query)
{
    // Required filters — always applied
    // (none in this example)

    // Optional filters — applied when non-null
    if (GuestId is not null)
        query = query.Where(e => e.GuestId == GuestId);
    if (Status is not null)
        query = query.Where(e => e.Status == Status);

    // Sort — default descending
    var checkInDir = CheckInSort ?? SortDirection.Descending;
    query = checkInDir == SortDirection.Ascending
        ? query.OrderBy(e => e.CheckIn)
        : query.OrderByDescending(e => e.CheckIn);

    return query;
}
```

The `Apply()` method is a **pure IQueryable transform** — it adds LINQ operators but does not execute anything. The executor calls it after global filters, so your `WHERE` conditions compose with soft-delete/tenant filters via `AND`.

### Order of Operations

The final SQL `WHERE` clause combines three sources:

```
WHERE
    /* 4b: Root filters */
    NOT IsDeleted
    AND TenantId = 'T1'

    /* 5: Your filters (from Apply) */
    AND GuestId = @p0
    AND Status = 'Confirmed'

ORDER BY CheckIn DESC
```

All three filter sources are composed into a single expression tree before EF Core translates it to SQL — there is no in-memory filtering.

---

## Step 6: Count + Page + Project

The executor runs two SQL queries for paged results:

```csharp
// Query 1: Count
var totalCount = await source.CountAsync(ct);

// Query 2: Data
var items = await source
    .Skip(query.Skip)          // (Page - 1) * PageSize
    .Take(query.Take)          // PageSize
    .Select(query.Projection)  // DTO mapping (if IQuery<T, R>)
    .ToListAsync(ct);
```

### Projection

If the query implements `IQuery<TEntity, TResult>`, the `Projection` expression is applied via
`Select()`. It is generated by **`[GenerateProjection]`** on the DTO — `[MapFrom<TEntity>]` on its own
produces `FromEntity` and `Selector`, which run in memory, and no `Projection` at all:

```csharp
// On the DTO:
[MapFrom<Reservation>]
public partial class ReservationSummaryDto
{
    public Guid Id { get; init; }
    public string GuestName { get; init; }
    public ReservationStatus Status { get; init; }
}

// Generated:
public static readonly Expression<Func<Reservation, ReservationSummaryDto>> FromEntity =
    e => new ReservationSummaryDto
    {
        Id = e.PersistenceId,
        GuestName = e.Guest.FirstName + " " + e.Guest.LastName,
        Status = e.Status
    };
```

Projections are translated to SQL `SELECT` — only the columns needed by the DTO are fetched from the database. This is significantly more efficient than loading full entities and mapping in memory.

### Caching

If the query is marked with `[Cacheable]`, the SG generates `ICacheable` implementation and the executor checks `ICacheStack` before hitting the database:

```csharp
[Cacheable(Duration = "5m", Tags = ["properties"])]
public partial class GetPopularProperties
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

// SG generates: GetCacheKey() → "GetPopularProperties:Page=1:PageSize=20"
// SG generates: GetCacheOptions() → CacheEntryOptions with 5 min duration + tags
```

The cache stores the `PagedResult<T>` — both the items and the total count. On cache hit, no SQL is executed.

---

## Step 7: PagedResult

The result carries data and paging metadata:

```csharp
var result = await executor.ExecuteAsync(query, source, ct);

// Success path
if (result.IsSuccess)
{
    result.Items       // IReadOnlyList<ReservationSummaryDto>
    result.TotalCount  // 47 (total across all pages)
    result.Page        // 2
    result.PageSize    // 20
    result.TotalPages  // 3
    result.HasNextPage // true
}

// Error path
if (result.IsFailure)
{
    result.Error       // QueryError with Type (Timeout, Connection, Database)
}
```

### Error Mapping

`EfCoreQueryExecutor` catches EF Core exceptions and maps them to typed errors:

| EF Core Exception | QueryError Type | Meaning |
|-------------------|-----------------|---------|
| `OperationCanceledException` | `Timeout` | Query exceeded timeout or was cancelled |
| `DbUpdateException` (connection) | `Connection` | Database connectivity issue |
| Other `DbUpdateException` | `Database` | Query translation or execution error |

---

## Without an Endpoint

You can use the query pipeline without endpoints — for service methods, background jobs, or tests:

```csharp
public class ReservationService(
    IQueryExecutor executor,
    IReadRepository<Reservation> reservations)
{
    public async Task<PagedResult<ReservationSummaryDto>> Search(
        Guid? guestId, ReservationStatus? status, CancellationToken ct)
    {
        var query = new SearchReservationsQuery
        {
            GuestId = guestId,
            Status = status,
            Page = 1,
            PageSize = 50
        };

        return await executor.ExecuteAsync(query, reservations.Query(), ct);
    }
}
```

The pipeline (filters, projection, paging) works the same way regardless of whether the query comes from an HTTP endpoint or a service method.

⚠️ Calling the executor directly skips the invoker: no validation, no permission — and no
`[FromCurrentUser]` binding. A bound property is always applied, so a query run this way filters on the
property's default — `Guid.Empty`, `""` — never on the caller, and never on nobody. From another module,
call the query's member on the boundary interface, which goes through the invoker.

---

## Query Interface Hierarchy

The source generator chooses the interface based on your query class:

```
IQuery<TEntity>                          ← Base: has Apply()
├── IQuery<TEntity, TResult>             ← + Projection expression
├── IPagedQuery<TEntity>                 ← + Page, PageSize, Skip, Take
│   └── IPagedQuery<TEntity, TResult>    ← Paged + Projection
├── IIncludableQuery<TEntity>            ← Mixin: IncludePaths
└── IQueryHints                          ← Mixin: NoTracking, SplitQuery, IgnoreGlobalFilters
```

| Your Query Has | Generated Interface |
|----------------|---------------------|
| `[Query<Order>]` | `IQuery<Order>` |
| `[Query<Order, OrderDto>]` | `IQuery<Order, OrderDto>` |
| `[Query<Order>]` + `Page`/`PageSize` | `IPagedQuery<Order>` |
| `[Query<Order, OrderDto>]` + `Page`/`PageSize` | `IPagedQuery<Order, OrderDto>` |

`IIncludableQuery` and `IQueryHints` are mixed in alongside the base — they don't change the base interface choice:

```csharp
// SG generates: IPagedQuery<Order, OrderDto>, IQueryHints, IIncludableQuery<Order>
[Query<Order, OrderDto>]
public partial class GetOrders : IQueryHints, IIncludableQuery<Order>
{
    public bool NoTracking => true;
    public bool SplitQuery => true;
    public IReadOnlyList<string> IncludePaths => ["Customer", "Lines.Product"];

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

---

## Performance Considerations

### When to Use Projections

**Always prefer `[Query<T, R>]` over `[Query<T>]`** for read endpoints. Projections:
- Fetch only the columns the DTO needs (less I/O, less memory)
- Skip change tracking entirely (`AsNoTracking` is the default)
- Avoid materializing navigation properties you don't need

Use `[Query<T>]` (without projection) only when you need full entities — typically for mutations or when the caller needs to modify and save the entity.

### When to Use SplitQuery

Activate `SplitQuery = true` when your query includes **multiple collection navigations** at the same depth:

```csharp
// Order has Items (collection) and Tags (collection)
// Without split: cartesian explosion → 3 items × 2 tags = 6 rows per order
// With split: 3 separate SQL queries, no duplication
[Query<Order, OrderDetailDto>]
public partial class GetOrderDetail : IQueryHints
{
    public bool SplitQuery => true;
}
```

### Filter Performance

All filters — root, navigation, and query — are composed into the expression tree before EF Core translates to SQL. There is **no in-memory filtering**. The database does all the work.

For complex filter combinations, check the generated SQL with EF Core logging:

```csharp
optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
```

---

## Extending the Pipeline

### Custom IQueryFilterProvider

Replace the default filter provider to add application-specific filter logic:

```csharp
services.AddScoped<IQueryFilterProvider, MyCustomFilterProvider>();
```

### Custom IQueryExecutor

Wrap or replace `EfCoreQueryExecutor` for cross-cutting concerns (telemetry, throttling):

```csharp
public class InstrumentedQueryExecutor(
    EfCoreQueryExecutor inner,
    ILogger<InstrumentedQueryExecutor> logger) : IQueryExecutor
{
    public async Task<PagedResult<TResult>> ExecuteAsync<TEntity, TResult>(
        IPagedQuery<TEntity, TResult> query, IQueryable<TEntity> source, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("Query");
        activity?.SetTag("query.type", query.GetType().Name);

        var result = await inner.ExecuteAsync(query, source, ct);

        activity?.SetTag("query.total_count", result.TotalCount);
        return result;
    }
}
```

### Pre/Post Processors on Endpoints

⚠️ **Not on a query**, and declaring them there is **PRAG0703**. `[PreProcessor<T>]` and
`[PostProcessor<T>]` are rendered by the handler templates for `Endpoint<T>` classes, mutations and
domain actions; the query handler template does not read them.

```csharp
// works — an Endpoint<T> class
[Endpoint(HttpVerb.Get, "api/v1/reservations/search")]
[PreProcessor<AuthorizationProcessor>]
[PostProcessor<AuditLogProcessor>]
public partial class SearchReservationsEndpoint : Endpoint<PagedResult<ReservationSummaryDto>> { }
```

For a query endpoint, the hooks that do run are the ones the generated delegate calls: the endpoint's
`ResourcePolicy`, an `IResourceAuthorizer<TQuery>`, and `ISyncValidator` on the query itself.

---

## Related Guides

- [Query System](09-query-system.md) — Declaring queries, filters, sorts, joins
- [Grid Filtering](10-grid-filtering.md) — Dynamic grid framework integration
- [Query Filters](07-query-filters.md) — Soft-delete, tenant, permission filters
- [Projections and Views](11-projections-views.md) — Computed expressions and aggregations
- [Data Sources](12-datasource-loading.md) — Loading strategies and profiles
- [Filter Pipeline](efcore/06-filter-pipeline.md) — Navigation filter internals
