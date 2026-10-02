---
title: "Query Filters"
description: "Query filters are one of the main reasons this persistence stack exists."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/07-query-filters.md
sidebar:
  order: 8
---
Query filters are one of the main reasons this persistence stack exists.

They let you define data visibility rules once and apply them consistently, instead of relying on every query author to remember the same `Where(...)` clauses.

For novice users, the most important mental model is:

- filters are safety rails
- they run automatically on generated repository reads
- you disable them only on purpose, in a clearly scoped block

## What problem query filters solve

In a real application, many rows should not be visible all the time:

- soft-deleted records should usually stay hidden
- tenant data should not leak across tenants
- temporal data may need "currently valid only" behavior
- row-level authorization may depend on the current user

Without query filters, each repository or handler must remember those conditions manually.

That is not a good reliability model.

## How the system works

There are three pieces:

1. Generated filters
   Produced from persistence attributes such as `[SoftDelete]`.
2. Runtime filter services
   `IQueryFilterProvider`, `IQueryFilterToggle`, and related infrastructure.
3. Generated repositories
   They call the filter provider automatically on read paths.

In EF Core, navigation filtering is also applied through the filter-map pipeline so `Include(...)` paths do not accidentally bypass the same rules.

## Which one to write

Four interfaces, and the choice is not a matter of taste: each changes how the filter composes with the
others, or when it is skipped. Picking the wrong one is silent — the filter runs, and hides or reveals
rows you did not mean.

| You want | Implement | What that buys |
|---|---|---|
| A rule that always narrows the set | `IQueryFilter<T>` | **AND**ed with every other restrictive filter |
| A rule a permission can lift | `IPermissionBasedFilter<T>` | skipped for a holder of `BypassPermission`, skipped in the trusted modes, and its presence is what makes an anonymous read **fail closed** |
| A rule that *widens* what is visible | `IScopeVisibilityFilter` | **OR**ed with the other visibility contributions, then ANDed with the restrictive ones |
| To mark a filter as the tenant one | `ITenantFilter` | the provider recognises it by type rather than by name |

### `IQueryFilter<T>` — the restrictive default

Everything that removes rows and has no exception: soft delete, an archived flag, a status the
application never shows.

```csharp
public sealed class NotArchivedFilter : IQueryFilter<Order>
{
    public int Priority => 100;
    public Expression<Func<Order, bool>> GetFilter() => o => !o.IsArchived;
}
```

Composed with **AND**. Two restrictive filters both apply, and adding one can only ever return fewer
rows.

### `IPermissionBasedFilter<T>` — restrictive, with a way out

The same thing plus a `BypassPermission`: the rule holds for everyone except the holders of that
permission. Reach for it whenever the sentence contains "unless".

```csharp
public sealed class OwnTeamOnlyFilter(ICurrentUser user) : IPermissionBasedFilter<Order>
{
    public string BypassPermission => "orders.view_all";
    public int Priority => 300;

    public Expression<Func<Order, bool>> GetFilter()
    {
        var teamId = user.Claims.TryGetValue("team_id", out var values) ? values[0] : "";
        return o => o.TeamId == teamId;
    }
}
```

Three things follow from the interface, and only the first is obvious:

- a caller holding `orders.view_all` reads unfiltered;
- the trusted modes — `Admin`, `Background`, `Raw` — skip it, which is what lets a job read
  without a user;
- ⚠️ its **presence** is what makes an anonymous read fail closed. `FailClosedWhenAnonymous` looks for a
  permission-based filter on the entity; an entity whose only guard is a plain `IQueryFilter<T>` that
  reads `ICurrentUser` does **not** fail closed — it evaluates the expression against an empty user and
  returns whatever that happens to match. If the rule depends on who is asking, say so with this
  interface rather than by reading the user inside a restrictive one.

### `IScopeVisibilityFilter` — additive

The one that exists because AND is the wrong operator. Ownership, materialised access scopes and
computed scope rules each say a row **is** visible; a row reachable through any of them should come
back, and ANDing them means a row must satisfy all three — which hides everything reachable through only
one.

It is a marker, not a second `GetFilter`, so it goes **beside** one of the two above. That is what the
generated filters do:

```csharp
// generated for [HasAccessScopes], priority 250
public sealed class ScopedDataFilter(IUserScopeResolver scopes, ICurrentUser user)
    : IPermissionBasedFilter<Invoice>, IScopeVisibilityFilter
```

The provider ORs every visibility filter together first, then ANDs the result with the restrictive
group.

### `ITenantFilter` — a marker the provider looks for

You rarely write one: an entity that is `ITenantEntity` gets a generated `TenantFilter` carrying it. It
exists so the provider can recognise the tenant filter by **type** rather than by name — a class of your
own called `TenantFilter` is not one, and this interface is what says so.

### Two dials that are not about which interface

`Priority` (lower first) decides the order of the AND terms. It does not change the result — a
conjunction is commutative — but it is what you read in the log, so keep the numbers meaningful. What
the generator emits, measured:

| Filter | Priority | Interfaces |
|---|---|---|
| `SoftDeleteFilter` | 100 | `IQueryFilter<T>` |
| `TenantFilter` | 200 | `IQueryFilter<T>`, `ITenantFilter` |
| `DataAccessFilter`, `ParentVisibilityFilter` | 200 | `IPermissionBasedFilter<T>` (+ `IScopeVisibilityFilter` for the first) |
| `ScopedDataFilter` | 250 | `IPermissionBasedFilter<T>`, `IScopeVisibilityFilter` |

`Scope` decides **where** the filter reaches: `Root`, `Collections`, `OptionalReferences`,
`RequiredReferences`, `Subqueries`, `Projections`, `Joins`, combined with `|`. The default covers the
ordinary read.

⚠️ On a **required** reference, `BehaviorOnRequired` decides what happens when the related row does not
match: `FilterNavigation` filters the navigation and can leave a null where the model says there cannot
be one, `FilterParent` drops the whole parent row, `Skip` leaves the relation alone. `FilterParent` is
the safe reading; `FilterNavigation` is the default. Worth knowing before a required navigation starts
coming back null.

## Registering generated filters

**In a Pragmatic host you register nothing.** The generated host calls the filter registration of
every module it discovers, from the persistence metadata, before your `IStartupStep` runs.

The extension it calls is `Add{Prefix}QueryFilters()`, in `_Infra.Persistence.QueryFilters.g.cs`, where
`{Prefix}` is the identifier derived from the common namespace of the entities — `AddGeneratedQueryFilters()`
when they share no prefix. Call it yourself only outside a host, next to the rest of the wiring:

```csharp
builder.Services.AddSalesDbContext(o => o.UseNpgsql(cs));
builder.Services.AddPragmaticPersistenceRepositories<SalesDbContext>();
builder.Services.AddMyAppQueryFilters();
```

⚠️ Repositories resolve perfectly well without that last line, and read **unfiltered**: no soft-delete,
no tenant, no ownership. Nothing fails, the rows simply are not restricted — which is why the host
stopped leaving the call to the application.

## Auto-generated filters

| Source | Generated effect | Typical purpose |
|--------|------------------|-----------------|
| `[SoftDelete]` | Adds a soft-delete filter | Hide deleted rows from normal reads |
| Tenant-aware entity shape | Adds a tenant filter | Restrict rows to the current tenant |
| Temporal relation metadata | Adds a temporal filter | Hide inactive historical rows by default |

You do not call these filters directly. The repository and query pipeline apply them for you.

## What a normal read looks like

Your code:

```csharp
var orders = await ordersRepository.FindAsync(
    Spec<Order>.Where(o => o.Total > 1000),
    ct);
```

What the repository effectively does:

- starts from the `DbSet<Order>`
- asks `IQueryFilterProvider` for the combined filter
- applies the filter before executing the query

That is why soft-delete and tenant rules stay consistent across the application.

## `IQueryFilterToggle`: temporary, scoped overrides

Sometimes you really do need to bypass a filter:

- admin recovery screens
- support tooling
- data repair scripts
- migration workflows

`IQueryFilterToggle` is the safe mechanism for that.

Example:

```csharp
public sealed class AdminOrdersService(
    IRepository<Order> orders,
    IQueryFilterToggle filterToggle)
{
    public Task<List<Order>> GetIncludingDeleted(CancellationToken ct)
    {
        using (filterToggle.Disable<Order.SoftDeleteFilter>())
        {
            return orders.Query().ToListAsync(ct);
        }
    }
}
```

Why the `using` block matters:

- the override is temporary
- the previous state is restored automatically
- the change stays local to the current async flow

Under the hood, `QueryFilterToggle` uses `AsyncLocal`, so the scope works correctly with `await`.

## Filter modes

If you need a broader behavioral switch, use `FilterMode`.

```csharp
using (filterToggle.UseMode(FilterMode.Admin))
{
    // Skip visibility and permission-based filters.
}

using (filterToggle.UseMode(FilterMode.Background))
{
    // Skip tenant, visibility, and permission-based filters.
}

using (filterToggle.UseMode(FilterMode.Raw))
{
    // Bypass all automatic filters.
}
```

Current modes:

The modes are **ordered**, and each one skips everything the one before it skips:

| Mode | Value | Skips |
|------|---|---|
| `Normal` | 0 | nothing — every filter applies |
| `Admin` | 1 | visibility and permission-based filters |
| `Background` | 3 | the above, plus the tenant filter |
| `Raw` | 4 | everything, soft-delete included |

⚠️ **Soft-delete survives every mode except `Raw`.** `Admin` does not reveal deleted rows — a common
assumption, and a wrong one. To read them, disable that one filter or go to `Raw`.

For novice teams, use `Raw` sparingly. It is the sharpest tool in the box.

### The rest of the toggle

`IQueryFilterToggle` is a little wider than the three calls above:

| Member | For |
|---|---|
| `Disable<TFilter>()` / `Disable(Type)` | Turn off one filter for the scope |
| `DisableAll()` | Turn off all of them |
| `UseMode(FilterMode)` | Switch mode for the scope |
| `IsDisabled<TFilter>()` / `IsDisabled(Type)` | Ask, without changing anything |
| `AllDisabled` · `CurrentMode` · `GetDisabledFilterTypes()` | What the current scope looks like |

## `DisableAll()` vs `UseMode(FilterMode.Raw)`

Both can result in "show me everything", but they communicate slightly different intent:

- `DisableAll()` means "turn off all filters in this scope"
- `UseMode(FilterMode.Raw)` means "run this query path in raw mode"

If you are writing application code, `UseMode(...)` is often easier to reason about because it expresses a mode change rather than a collection of disabled filters.

## `FilterContext`

At runtime, filters evaluate against a `FilterContext`.

It carries information such as:

- current tenant
- current user ID
- current timestamp
- disabled filter types
- current mode

That is how the same filter infrastructure can support:

- tenant filtering
- time-aware filtering
- permission-aware behavior
- admin or migration bypasses

## Permission-based filters

When a filter depends on the current user's permissions, implement `IPermissionBasedFilter<T>`.

Beyond `GetFilter()` and `Priority`, `IQueryFilter<T>` carries three members that decide **where** a
filter applies — all with defaults, so you override only what you mean to change:

| Member | Default | What it decides |
|---|---|---|
| `Scope` | `Root \| Collections \| Subqueries \| Projections \| Joins \| OptionalReferences` | Which parts of a query the filter reaches |
| `ShouldApplyTo(NavigationContext)` | `true` | Per-navigation say, for what `Scope` cannot express |
| `BehaviorOnRequired` | `FilterNavigation` | What to do at a **required** navigation: filter it, filter the parent, or skip |

`FilterScope` is a flags enum: `None`, `Root`, `Collections`, `OptionalReferences`,
`RequiredReferences`, `Subqueries`, `Projections`, `Joins`, plus the combinations `AllNavigations`,
`Default` and `All`.

A collection navigation is filtered according to **where the query reads it**, decided by the operator
that reaches it — and whatever is nested inside that operator's lambda takes the same position:

| Flag | Where |
|---|---|
| `Collections` | an `Include` / `ThenInclude` — the collection loaded with its entity |
| `Subqueries` | every operator that is not a projection: `Where`, `OrderBy`, `Any`, `All`, `Count`… |
| `Projections` | `Select`, `SelectMany`, `GroupBy`, a join's result — aggregates included |
| `Joins` | the set a declared `[Join<T>]` reads, filtered before it is joined |

The default holds all four, because a row read through a predicate, a projection or a join is the same
row the filter withholds at the root. Take one out to leave that position alone —
`FilterScope.Default & ~FilterScope.Projections` filters an `Include` and a `Where` and not a `Select`.
⚠️ Required references are not in the default, and references of either kind are not visited yet:
`OptionalReferences` and `RequiredReferences` change nothing today. The soft-delete, tenant and validity
filters the generator writes into the navigation map apply in every position.

Example:

```csharp
using System.Linq.Expressions;
using Pragmatic.Identity;
using Pragmatic.Persistence.Query.Filters;

public sealed class TeamOrdersFilter(ICurrentUser user) : IPermissionBasedFilter<Order>
{
    public string BypassPermission => "orders.view_all";
    public int Priority => 300;

    public Expression<Func<Order, bool>> GetFilter()
    {
        // Claims maps a name to a LIST of values — a claim can legitimately appear more than once
        var teamId = user.Claims.TryGetValue("team_id", out var values) ? values[0] : "";
        return order => order.TeamId == teamId;
    }
}
```

### What happens when there is no user

⚠️ **The query returns nothing.** This is the opposite of what most people assume.

`DefaultQueryFilterProvider` checks, *before* it looks at any individual filter: is the caller
unauthenticated, does this entity have at least one permission-based filter, and is the mode not an
elevated one? If all three hold it returns `_ => false` — a filter that matches no row — rather than
dropping the permission filter and answering with everything. The option is
`QueryFilterOptions.FailClosedWhenAnonymous`, and it defaults to `true`.

```csharp
// Fail open instead — an [AllowAnonymous] endpoint that intentionally
// exposes a permission-filtered entity is the case it exists for
services.AddSingleton(new QueryFilterOptions { FailClosedWhenAnonymous = false });
```

The trusted modes are unaffected: `Admin`, `Background` and `Raw` skip permission filters
by design, so a background job reads without a user and without failing closed.

The two remaining rules are per filter, and they run after that check:

- the user holds `BypassPermission` → that filter is skipped for them;
- there is no user at all *and* fail-closed is off → permission filters are skipped.

Register custom filters in DI as `IQueryFilter` — the provider takes them as an
`IEnumerable<IQueryFilter>`, so the non-generic interface is the registration:

```csharp
services.AddScoped<IQueryFilter, TeamOrdersFilter>();
```

Scoped, not singleton: the filter reads `ICurrentUser`, which is request-scoped.

---

## A filter end to end

Everything above in one worked case. The entity is soft-deletable, tenant-aware, and carries a team:

```csharp
[Entity]
[SoftDelete]
[BelongsTo<SalesBoundary>]
public partial class Order : IEntity, ITenantEntity
{
    // public setter, not private: ITenantEntity requires one, because TenantInterceptor stamps it
    // on insert from outside any object initializer. Treat it as infrastructure and do not assign it.
    public string TenantId { get; set; } = "";

    public string TeamId { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

Two filters are generated from that declaration — `Order.SoftDeleteFilter` (priority 100) and
`Order.TenantFilter` (200). The third is yours:

```csharp
public sealed class TeamOrdersFilter(ICurrentUser user) : IPermissionBasedFilter<Order>
{
    public string BypassPermission => "orders.view_all";
    public int Priority => 300;

    public Expression<Func<Order, bool>> GetFilter()
    {
        var teamId = user.Claims.TryGetValue("team_id", out var values) ? values[0] : "";
        return order => order.TeamId == teamId;
    }
}
```

```csharp
services.AddScoped<IQueryFilter, TeamOrdersFilter>();
```

### What the provider does, in order

A read through the generated repository — `orders.FindAsync(spec, ct)` — goes through these steps
before your specification is even applied:

1. **Build the `FilterContext`** from the tenant context, the current user, the clock and the toggle:
   `Mode`, `DisabledFilters`, `TenantId`, `UserId`, `Now`.
2. **Raw?** `Mode == FilterMode.Raw` returns `null` immediately — no filter, not even soft-delete.
3. **Fail closed?** Anonymous, a permission-based filter exists for `Order`, mode below `Admin` →
   `_ => false`. The query runs and returns nothing.
4. **Select the filters for `Order`**, ordered by `Priority`, dropping each one that is disabled by the
   toggle, named in `DisabledFilters` (by entity type *or* by filter type), skipped by the mode, or
   bypassed by the user's permission.
5. **Compose them.** Restrictive filters are ANDed. Scope-visibility filters — the ones implementing
   `IScopeVisibilityFilter`: ownership, scoped data, computed scope rules — are **ORed with each
   other** first, because they are additive: a row is visible if it matches *any* of them. The two
   groups are then ANDed together.
6. **Apply**: `query.Where(combined)`.

For an authenticated user in team `t-7`, tenant `acme`, with none of it disabled, the result is one
expression:

```csharp
e => !e.IsDeleted && e.TenantId == "acme" && e.TeamId == "t-7"
```

```sql
SELECT ... FROM "Orders" AS o
WHERE NOT o."IsDeleted" AND o."TenantId" = @__tenant AND o."TeamId" = @__team
```

The AND terms follow the priorities: 100, 200, 300. Order does not change the result, but it is what
you will see in the log.

### The five ways it does not apply

| What you write | What happens to `TeamOrdersFilter` | And to the others |
|---|---|---|
| nothing — an ordinary read | applies | apply |
| the user has `orders.view_all` | skipped | apply |
| `toggle.Disable<TeamOrdersFilter>()` | skipped | apply |
| `toggle.UseMode(FilterMode.Admin)` | skipped — it is permission-based | soft-delete and tenant still apply |
| `toggle.UseMode(FilterMode.Raw)` | skipped | **none apply**, deleted rows included |
| no authenticated user | **the query returns nothing** | irrelevant — nothing is composed |

### Testing it

Assert the two halves separately: that the filter restricts, and that the escape hatch opens.

```csharp
var user = FakeCurrentUser.Authenticated("u-1");   // in team t-7 via claims
services.AddScoped<ICurrentUser>(_ => user);
services.AddScoped<IQueryFilter, TeamOrdersFilter>();

// restricts
var mine = await orders.FindAsync(Spec<Order>.True, ct);
mine.Should().OnlyContain(o => o.TeamId == "t-7");

// and opens, for exactly one filter
using (toggle.Disable<TeamOrdersFilter>())
{
    var all = await orders.FindAsync(Spec<Order>.True, ct);
    all.Should().Contain(o => o.TeamId != "t-7");
}
```

⚠️ A test that only asserts the first half passes against a provider that never registered the filter
at all. The second half is what tells the two apart.

## Dynamic visibility filters

Some filters cannot be generated at compile time because they depend on runtime data from another service.

That is what `IVisibilityFilterProvider` is for.

Example:

```csharp
using System.Linq.Expressions;
using Pragmatic.Persistence.Query.Filters;

public sealed class TeamVisibilityProvider(ITeamService teams) : IVisibilityFilterProvider
{
    public IReadOnlyDictionary<Type, LambdaExpression> GetFilters(FilterContext context)
    {
        if (context.SkipVisibility)
            return new Dictionary<Type, LambdaExpression>();

        var teamIds = teams.GetVisibleTeamIds(context.UserId);

        return new Dictionary<Type, LambdaExpression>
        {
            [typeof(Project)] = (Expression<Func<Project, bool>>)(project => teamIds.Contains(project.TeamId))
        };
    }
}
```

Register it like any other scoped runtime service:

```csharp
services.AddScoped<IVisibilityFilterProvider, TeamVisibilityProvider>();
```

`FilterMapComposer` merges these runtime filters with the source-generated filter map used for navigation filtering.

## Navigation filtering

A common novice question is:

"If I include child collections, do filters still apply there?"

Yes, for collections — wherever the query reads them, as far as the filter's `Scope` says (see the
table above; the default says everywhere). A collection navigation is filtered in an
`Include`/`ThenInclude` (as EF's filtered include), in a predicate (`o.Items.Any(…)`,
`o.Items.Count > 0`), and inside a projection or an aggregate, which the executor filters after composing
them. A navigation declared as `ICollection<T>`, `IList<T>`, `List<T>`, `IReadOnlyCollection<T>`,
`IReadOnlyList<T>` or `IEnumerable<T>` keeps its type; one of another type (a `HashSet<T>`) that a filter
guards is refused with an exception rather than read unfiltered. Required references are not reached.

A declared join (`[Join<T>(ForeignKey = …)]`) reads a set of `T` filtered for the caller before the
join, and a cached query that joins is partitioned per caller like one that reads a guarded collection.

Example:

```csharp
var order = await orderRepository.GetByIdAsync(
    orderId,
    query => query.Include(o => o.Items),
    ct);
```

The soft-delete filter reaches that `Include` because collections are in the default scope: the
children come back filtered, not wholesale.

This is handled by the generated filter map plus `FilterMapComposer` in the EF Core layer.

## Common mistakes

### Forgetting to register generated query filters

Only possible outside a Pragmatic host, since the host makes the call itself. When it does happen the
repository resolves and reads **unfiltered** — deleted rows come back, tenants see each other. Nothing
throws, which is what makes it worth checking for.

### Disabling filters too broadly

Prefer the narrowest possible scope:

- disable one filter if that is enough
- use `Admin` or `Background` mode when the behavior matches the use case
- reserve `Raw` for migrations, support, or very explicit low-level paths

### Treating filter bypass as a normal business path

If a screen or endpoint always needs raw data, that is often a modeling smell. Revisit the business rule before normalizing widespread filter bypass.

### Assuming interface methods expose everything

Filters affect repository reads, but helper methods like `GetBySkuAsync(...)` still live on the concrete generated repository, not on `IRepository<TEntity>`.

## Testing guidance

When testing filter behavior:

- register generated query filters in the test DI container
- resolve `IQueryFilterToggle` from DI
- assert both the default filtered behavior and the explicitly disabled behavior

For a full test harness, see [Testing Generated Persistence](/modules/persistence/efcore-07-testing-generated-persistence/).

## Recommended follow-up reading

- [Repository](/modules/persistence/05-repository/)
- [Diagnostics Guide](/modules/persistence/14-diagnostics/)
- [Testing Generated Persistence](/modules/persistence/efcore-07-testing-generated-persistence/)
- [Filter Pipeline](/modules/persistence/efcore-06-filter-pipeline/)
