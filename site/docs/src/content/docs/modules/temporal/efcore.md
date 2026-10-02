---
title: "EF Core Integration"
description: "`Pragmatic.Temporal.EFCore` maps every temporal type to a sensible database column and adds timezone-correct LINQ query helpers. Namespace: `Pragmatic.Temporal."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/efcore.md
sidebar:
  order: 7
---
`Pragmatic.Temporal.EFCore` maps every temporal type to a sensible database column and adds timezone-correct LINQ query helpers. Namespace: `Pragmatic.Temporal.EntityFrameworkCore`.

---

## Activating the Value Converters

**When you need it**: your entities have `LocalDate`, `Duration`, `ZonedDateTime`, … properties and EF Core must know how to store them. Without activation, model building fails with "could not be mapped" errors.

**What you write** — one line on the options builder:

```csharp
services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .UsePragmaticTemporal());
```

**What you get**: every property of a temporal type, on every entity, gets the right value converter and max-length automatically — nothing to configure per property, no `OnModelCreating` code.

Two options are available:

```csharp
options.UsePragmaticTemporal(t =>
{
    t.StoreDurationAsTicks = false;   // store Duration as TimeSpan instead of ticks (bigint)
    t.ApplyToAllProperties = false;   // opt out of the automatic convention entirely
});
```

### Manual alternatives

If you prefer wiring the convention yourself (e.g. the options builder is out of your control), the two mechanisms below produce the identical model:

```csharp
// In ConfigureConventions
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.Conventions.Add(_ => new TemporalModelConvention());
}
```

```csharp
// Or at the end of OnModelCreating
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ... your entity configuration ...
    modelBuilder.ApplyTemporalConventions();   // accepts an optional TemporalEfCoreOptions
}
```

---

## Column Mapping

| CLR type | Column type | Stored as | MaxLength |
|----------|------------|-----------|-----------|
| `LocalDate` / `LocalDate?` | `date` | native `DateOnly` | — |
| `LocalTime` / `LocalTime?` | `time` | native `TimeOnly` | — |
| `LocalDateTime` / `LocalDateTime?` | `datetime` | `DateTime`, **no timezone conversion**, read back as `Kind.Unspecified` | — |
| `ZonedDateTime` / `ZonedDateTime?` | `nvarchar` | string `"2026-06-01T14:30:00+02:00[Europe/Rome]"` | 100 |
| `Duration` / `Duration?` | `bigint` | **ticks** (`long`) | — |
| `Period` / `Period?` | `nvarchar` | ISO string `"P1Y2M3D"` | 50 |
| `DateRange` / `DateRange?` | `nvarchar` | string `"2026-01-01/2026-12-31"` | 50 |
| `CronExpression` (+ nullable) | `nvarchar` | raw cron string | 100 |

Details worth knowing:

- **`Duration` is ticks by default.** For databases with a native interval type, set `StoreDurationAsTicks = false` in `UsePragmaticTemporal(...)` (or pass options to `ApplyTemporalConventions`) and every `Duration` column becomes `TimeSpan` instead.
- **`DateRange` as a single string is not range-queryable in SQL.** If you need `WHERE date BETWEEN start AND end` server-side, model the range as an owned type with two `date` columns instead and keep `DateRange` for the domain layer.
- **String-stored types round-trip through `Parse`**: corrupt data in the column throws at materialization, not silently.
- **Golden rule reminder**: store instants as UTC `DateTimeOffset` columns. `ZonedDateTime` columns are for the rare cases where the *zone identity itself* is domain data (e.g. a venue's timezone attached to an event time).

---

## Timezone-Correct Query Extensions

**When you need it**: filtering UTC timestamp columns by "a day / month / range *in some timezone*" without writing DST-buggy boilerplate and without breaking index usage.

All methods live in `Pragmatic.Temporal.EntityFrameworkCore.QueryExtensions` and take an `Expression<Func<T, DateTimeOffset>>` selector — they target **plain UTC `DateTimeOffset` columns** (your `CreatedAt`, `OccurredAt`, …), not the converted temporal types above. Every method compiles to a pre-computed UTC range (`>= start && < end`) so indexes keep working; no conversion functions ever appear inside the SQL `WHERE`.

**What you write**:

```csharp
var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");

// All orders placed on a specific Rome calendar day
var day = await db.Orders
    .WhereDate(o => o.CreatedAt, new LocalDate(2026, 6, 1), rome)
    .ToListAsync();

// "Today" for the current request (business timezone from TemporalContext)
var today = await db.Orders.WhereToday(o => o.CreatedAt, temporalContext).ToListAsync();

// Reporting periods
var june   = await db.Orders.WhereMonth(o => o.CreatedAt, 2026, 6, rome).ToListAsync();
var q2     = await db.Orders.WhereQuarter(o => o.CreatedAt, 2026, 2, rome).ToListAsync();
var last7  = await db.Orders.WhereLast(o => o.CreatedAt, 7, temporalContext).ToListAsync();
```

### Method reference and boundary semantics

| Method | Filters on | Boundaries |
|--------|-----------|------------|
| `WhereBetween(selector, start, end)` | explicit UTC range | `start` inclusive, `end` **exclusive** |
| `AsOf(selector, moment)` | point-in-time | `<= moment` (inclusive) |
| `WhereDate(selector, date, zone)` | one calendar day in `zone` | full local day |
| `WhereToday(selector, ctx)` | today in **business** timezone | full local day |
| `WhereClientToday(selector, ctx)` | today in **client** timezone | full local day |
| `WhereBetweenDates(selector, from, to, zone)` | date range in `zone` | `to` **inclusive** (whole end day) |
| `WhereMonth / WhereYear / WhereQuarter(…, zone)` | calendar period in `zone` | whole period; `quarter` validated 1–4 |
| `WhereWeek(selector, year, week, zone, firstDay)` | ISO 8601 week | whole week; `week` is **not** range-validated |
| `WhereLast(selector, days, ctx)` | last N days, business tz | **includes today** (N=7 → today + 6 previous) |
| `WhereLastMonth / WhereThisMonth / WhereThisYear(…, ctx)` | relative periods, business tz | whole period |

### Caveats

- Day boundaries are computed DST-safely (midnight in a DST gap shifts forward; ambiguous midnight uses standard time) — consistent with `TemporalContext.ClientStartOfDay`.
- Range bounds are embedded as constants (`Expression.Constant`), not parameters: each distinct range produces a distinct SQL text, which can pressure the query plan cache under high cardinality of ranges.
- The relative methods (`WhereToday`, `WhereLast`, …) take the ambient `TemporalContext` — inject it (scoped) rather than constructing zones by hand.

---

## See Also

- [Concepts](/modules/temporal/concepts/) — why instants are stored UTC and converted only at boundaries
- [ASP.NET Core Integration](/modules/temporal/aspnetcore/) — where the per-request `TemporalContext` comes from
