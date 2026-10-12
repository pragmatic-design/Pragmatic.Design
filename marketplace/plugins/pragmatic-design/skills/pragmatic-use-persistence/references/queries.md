# Declarative queries: the rest of the surface

Part of `pragmatic-use-persistence`, which keeps what most tasks need. This file holds
what a task reads only when it reaches it.

**Rung 3: run a declared query where you already are.**

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

**Rung 1: publish a rule you already wrote.** `[Query]` sits on a **static** member returning
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
the same member name in one namespace would derive one type twice: **PRAG0726**, an error. Rename
one, or move it to a namespace of its own.

`[Filter(IgnoreCase = true)]` compares without case, on string properties only.

**A declared default is honoured.** `public State? Status { get; init; } = State.Pending;` means the
listing shows pending rows when the caller says nothing, and `PageSize = 50` means fifty. The generated
endpoint keeps the initializer where the query string omits the parameter, and paging falls back to the
framework's `1`/`20` only for a query that declares neither.

⚠️ Consequence worth stating: with a default, a caller can no longer ask for "no filter" by omitting the
parameter: that is what declaring one means. Leave the property without an initializer if omitting it
should mean *everything*.

**Reading exactly one row: `Single = true`.** A get-by-id is a query with one filter and no paging,
so say that instead of hand-writing an endpoint with a repository and a `NotFoundError`:

```csharp
[Query<Order, OrderDetail>(Single = true)]
[Endpoint(HttpVerb.Get, "api/orders/by-id/{id}")]
public partial class GetOrderQuery
{
    public required Guid Id { get; init; }
}
```

The endpoint returns the DTO itself rather than a list, and answers **404** when nothing matches:
the executor takes the first row (`FirstOrDefault`, not `Single`), so a filter that is not unique is
not an error. Without `Single` the same query returns a list, and an empty result is a 200 with `[]`.

`Id` needs no `MapTo`: a filter on the entity's generated `Id` targets `PersistenceId`, the column
behind it, in single and list queries alike (`List<Guid>? Id` filters the key with `In`).

**Reading by the caller: `[FromCurrentUser]`.** "My balances", "my profile": the filter is who is
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
- `{ get; private set; }` (**PRAG0730** otherwise). It is never a query-string, route or body
  parameter, never in OpenAPI, never an argument of the boundary member; it **is** in the `[Cacheable]`
  key. A bound property is a filter, always applied and matched with `==`; no `[Filter]` needed.
- **PRAG0731** when it cannot be generated: unknown member, different type, `nameof` over another type,
  no `[PragmaticUser]`/resolver, member-less form on a non-`string`.
- ⚠️ Do not inject `ICurrentUser` into a query instead: the answer must be a function of the
  properties, or the cache key, the serialized query and the contract cannot see what changed it.

**Reading by the clock: `[FromClock]`, with a `[ComputedFilter]` method.** "Who is away today": the
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
  specification's expression (composed, parameterized). ⚠️ Its arguments cannot come from the row; a
  column, a lambda parameter of the body: **PRAG0735**.
- ⚠️ Not `DateTime.UtcNow` in a `[ComputedFilter]` property: the database evaluates it with its own clock,
  and a clock the host or a test injects never reaches it.
- `[FromClock]` is no filter and no parameter (query string, route, OpenAPI, boundary); it is in the
  `[Cacheable]` key. The invoker needs an `IClock` registered (Temporal does it).
- Both attributes work on a `[DomainAction]` and a `[Mutation]` too, with the same rules (the decision
  stamped with `Now`, the withdrawal refused from `Today`) instead of an injected `IClock` or
  `ICurrentUser`: see `pragmatic-use-actions-endpoints` → *What does not go in `Execute`*.
- **PRAG0733**: a `[ComputedFilter]` the generator cannot write. **PRAG0734**: a `[FromClock]` on a type
  the clock does not give, or with a setter anyone else reaches.

**Counting instead of listing: a query whose result is a view.** A `GROUP BY` answers with rows that
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

⚠️ A `[QueryView]` on its own generates only a `Build(IQueryable<T>)`, with no route and no entry in the
published contract. Naming it as a query's result is what gives an aggregate an HTTP surface.
