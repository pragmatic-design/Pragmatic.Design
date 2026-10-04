# Temporal: API surface

Two different things carry the word, and confusing them is the first mistake:

- **`Pragmatic.Temporal`**, the module: the clock, timezones, business days, and the six attributes that
  convert instants on the way in and out of an API.
- **`[TemporalRelation<TParent>]`**, a persistence feature: rows with a validity period, one active at a
  time, with history. Documented in the persistence guide, not here.

---

## The golden rule

Store instants in **UTC**. Every conversion happens at the edge (serialization, model binding) and
never in a handler. The attributes below are how you say which conversion, and the generator writes the
registration.

## The six conversion attributes

`Pragmatic.Temporal.Attributes`, on a `DateTime` / `DateTimeOffset` property (nullable included). Any
other property type is ignored.

| Attribute | Direction | Effect |
|---|---|---|
| `[AsUtc]` | in + out | input without an offset is read as UTC; output is always emitted in UTC |
| `[FromClientTimezone]` | in | wall time without an offset is interpreted in the caller's zone, DST-safe |
| `[ToClientTimezone]` | out | the instant is emitted in the caller's zone |
| `[FromBusinessTimezone]` | in | wall time without an offset is interpreted in the application's business zone |
| `[ToBusinessTimezone]` | out | the instant is emitted in the business zone |
| `[KeepTimezone]` | in + out | the offset the caller sent is preserved rather than normalised |

```csharp
public sealed class OrderResponse
{
    [ToClientTimezone] public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateOrderRequest
{
    [FromClientTimezone] public DateTime RequestedDelivery { get; set; }
}
```

An explicit offset in the payload always wins over the attribute's interpretation: the attribute says
what to do when the caller did **not** say.

## What the generator writes

One registration per assembly, from every property it found:

```
{Prefix}TemporalBehaviorExtensions.Add{Prefix}TemporalBehaviors(IServiceCollection)
```

`{Prefix}` is the assembly's namespace prefix identifier: `ContosoSales` for `Contoso.Sales`. On a
Pragmatic host the generated host registration calls it; a standalone app calls it once at startup, or
registers by hand:

```csharp
TemporalJsonBehaviorRegistry.Register<OrderResponse>(
    nameof(OrderResponse.CreatedAt), TemporalJsonBehavior.ToClientTimezone);
```

⚠️ The attributes are read **per property, per assembly**. A DTO in a package you do not own carries no
behaviour unless that package generated its own registration; the attribute is not inherited through a
type reference.

## Reading the clock

Never `DateTime.UtcNow` in code you intend to test. Inject `IClock` (or `TimeProvider`), which the host
registers, and a test freezes it:

```csharp
[DomainAction]
public partial class ExpireOrdersAction : VoidDomainAction
{
    [FromClock] public DateTimeOffset Now { get; private set; }   // written by the invoker from IClock
    private IRepository<Order> _orders = null!;                  // injected by the generator

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var overdue = await _orders.Query().Where(o => o.DueAt < Now).ToListAsync(ct);
        // … mark them expired
        return VoidResult<IError>.Success();
    }
}
```

`[FromClock]` (`Pragmatic.Temporal.Clock`) takes `DateTimeOffset` (`UtcNow`) or `DateOnly` (`UtcToday`),
`{ get; private set; }`, else **PRAG0734**. Inject `IClock` itself only for what the property cannot
give: a second reading, a zone conversion.

## Querying by time

Two shapes, and they are not interchangeable:

**An instant comparison** is an ordinary filter, with nothing temporal about it beyond the column type:

```csharp
[Query<Order, OrderDto>]
public partial class OverdueOrdersQuery
{
    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "DueAt")]
    public DateTimeOffset? Before { get; init; }
}
```

**A validity period** is `[TemporalRelation]` on the entity (`[TemporalRelation<TParent>]` or
`[TemporalRelation<TParent, TChild>]` to scope it to a parent), and the generator answers with query
extensions on `IQueryable<T>`. A generated `TemporalFilter` hides every stretch not active **now**:

```csharp
public static class UserRoleTemporalExtensions
{
    public static IQueryable<UserRole> Active(this IQueryable<UserRole> query, TimeProvider? timeProvider = null);
    public static IQueryable<UserRole> ActiveAt(this IQueryable<UserRole> query, DateTimeOffset date);
    // with a parent (and its foreign key):
    public static IQueryable<UserRole> ForUser(this IQueryable<UserRole> query, Guid parentId);
    public static IQueryable<UserRole> ActiveForUser(this IQueryable<UserRole> query, Guid parentId);
}
```

**To read the history, lift the filter before the query is built**: a scope on the entity, not an
extension on the queryable (a method downstream of the query cannot widen what the filter already
narrowed):

```csharp
using (UserRole.IncludeHistory(filters))           // IQueryFilterToggle; keeps tenant and soft-delete
{
    var all = await repository.Query().ForUser(userId).ToListAsync(ct);
}
```

It also writes `ValidateTemporalConstraints(existing)` → `TemporalOverlapError?`, and with
`MaxActive = 1` a static `AutoClosePrevious(existing, closedAt[, parentId])`, which closes what is open
so the next stretch can start. ⚠️ Hand it the history, opening `IncludeHistory(filters)` around the call:
`repository.Query()` alone is already narrowed to what is active now, so a stretch starting tomorrow is
invisible and nothing is closed. `MaxActive = 1` is also a partial unique index, so that mistake is
refused by the database instead of corrupting the history.

## Where to read more

`Pragmatic.Temporal/docs/`: `aspnetcore.md` for binding and the attributes end to end, `dst-handling.md`
for the cases where a wall time does not exist or exists twice, `business-days.md`, `testing.md` for
freezing the clock.
