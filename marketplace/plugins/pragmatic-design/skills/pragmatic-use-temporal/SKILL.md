---
name: pragmatic-use-temporal
description: Use when code reads the current time, stores or converts dates, counts working days, schedules with cron, or when PRAG0900-0904 or PRAG0690 fire — Pragmatic.Temporal, IClock, [FromClock], time zones, TestClock.
---

# Pragmatic Use Temporal

**Covers:** Handle time with Pragmatic.Temporal — IClock and [FromClock] instead of DateTime.Now, business and client time zones, LocalDate/ZonedDateTime/DateRange, business days and holidays, cron, DST, EF Core mapping, TestClock.

Two rules carry most of the module:

1. **Store instants in UTC and convert at the edge** — model binding and serialization — never in a
   handler.
2. **Nothing reads the machine's clock.** "Now" comes from `IClock`, so a test can freeze it and a job
   and a request agree on what today is.

## Wiring

On a Pragmatic host the generator registers the module when the package is referenced; the host says
what only it knows:

```csharp
app.UseTemporal(t => t
    .UseBusinessTimeZone("Europe/Rome")        // the application's own zone: opening hours, deadlines
    .UseDefaultTimeZone("Europe/Rome")         // a caller who says nothing is in this one
    .UseHolidayProvider<CompanyHolidays>());   // default: no holidays, weekends only
```

`UseClock(clock)` replaces the clock (tests). In a plain host: `services.AddPragmaticTemporal(o => …)`.
Time-zone ids are IANA (`Europe/Rome`), resolved the same on Windows and Linux.

## Now

```csharp
[DomainAction]
public partial class ExpireOverdueOrders : VoidDomainAction
{
    [FromClock] public DateTimeOffset Now { get; private set; }   // written by the invoker from IClock
    private IRepository<Order> _orders = null!;
    // …
}
```

`[FromClock]` takes `DateTimeOffset` (`UtcNow`) or `DateOnly` (`UtcToday`), `{ get; private set; }` —
otherwise **PRAG0734**. Elsewhere inject `IClock`: `UtcNow`, `Now`, `UtcToday`, `Today`, `TimeOfDay`,
`GetTimeProvider()` for BCL APIs that take a `TimeProvider`. A host that binds `[FromClock]` without a
clock registered is **PRAG1698** at build time.

The analyzers keep the machine's clock out: **PRAG0900** `DateTime.Now`/`UtcNow`, **PRAG0901**
`DateTime.Today`, **PRAG0902** a `DateTime` without a `DateTimeKind`, **PRAG0903** `DateTimeOffset`
compared with `<`/`>` (compare `.UtcDateTime`), **PRAG0904** the same in test code.

## At the API edge

Declare how a value crosses the wire; the generator writes the registration per assembly:

| Attribute | Direction | Effect |
|---|---|---|
| `[AsUtc]` | in + out | no offset → read as UTC; always written in UTC |
| `[FromClientTimezone]` | in | wall time without an offset is the caller's local time |
| `[ToClientTimezone]` | out | written in the caller's zone |
| `[FromBusinessTimezone]` | in | wall time without an offset is the business zone's |
| `[ToBusinessTimezone]` | out | written in the business zone |
| `[KeepTimezone]` | in + out | the offset the caller sent is preserved |

An explicit offset in the payload always wins. ⚠️ The attributes are read **per property, in the
assembly that declares the type** — a DTO from a package that did not generate its own registration
carries none. Full detail: `../pragmatic-ecosystem/references/api-surface/temporal.md`.

## Types — pick by meaning

| Type | Is | Use for |
|---|---|---|
| `DateTimeOffset` (UTC) | an instant | timestamps, "when it happened" — what the database stores |
| `LocalDate` (≈ `DateOnly`) | a calendar day, no zone | birthdays, due dates, leave days |
| `LocalTime` | a time of day | opening hours |
| `LocalDateTime` | a wall-clock moment, no zone | what a person typed before you know their zone |
| `ZonedDateTime` | a wall-clock moment in a zone | "9:00 in Rome", arithmetic across DST |
| `Duration` | elapsed physical time | timeouts, SLAs in hours |
| `Period` | calendar units (months, days) | "one month later", age |
| `DateRange` | `[start, end]` of days | stays, leave requests; overlap, union, enumeration |
| `CronExpression` | a recurring schedule | stored schedules, evaluated in a zone |

⚠️ **`Duration` is not calendar arithmetic.** "One day later" across a DST change is 23 or 25 hours:
`zoned.AddDays(1)` keeps the wall time, `zoned.Add(Duration.FromHours(24))` keeps the elapsed time. Say
which one you mean.

⚠️ A wall time that does not exist (spring-forward) or exists twice (fall-back) has to be resolved: be
strict at input — refuse and ask — and forgiving in background work; `SetBeforeRomeSpringForward()` on
`TestClock` puts a test right before one.

## Business days

```csharp
public sealed class PaymentTerms(ITemporalCalculator calendar, IClock clock)
{
    public LocalDate DueDate(int workingDays) => calendar.AddBusinessDays(clock.Today, workingDays, "IT");
}
```

- `AddBusinessDays`, `CountBusinessDays`, `IsBusinessDay`, `IsHoliday`, `NextBusinessDay`,
  `PreviousBusinessDay`, period starts and ends.
- ⚠️ **Without a country code only weekends count** — Christmas on a Thursday is a business day for
  `IsBusinessDay(date)`.
- ⚠️ `CountBusinessDays(from, to)` is `[from, to)`: the end is excluded.
- `Next/PreviousBusinessDay` return a day strictly after/before, even when the day itself is one.
- The module ships **no holiday data**: implement `IHolidayProvider` (a table, a service such as the
  free Nager.Date API, cached per year and country), or build a `StaticHolidayProvider` for a fixed list.
  Company closures are holidays of your provider.

## Schedules

`CronExpression.Parse("0 8 * * 1-5")` (5 or 6 fields, `L`, `W`, `#`, names, `@daily`…),
`GetNextOccurrence(from, zone)`, `GetOccurrences(from, until, zone)`. Evaluated in a zone, results in UTC,
DST handled: an interval schedule fires in both passes of a repeated hour, a fixed-time one once.
Recurring **jobs** use this through `pragmatic-use-jobs`.

## Storage

Reference `Pragmatic.Temporal.EFCore` and the generated `DbContext` registration calls
`UsePragmaticTemporal()`: the temporal types get their columns (`LocalDate` → `date`, …) and stored
`DateTimeOffset`s are normalised to UTC. A host that stores instants without it gets **PRAG0690**: add
the package. When the wall-clock value really is the intent, say so in the boundary's database
configuration — `UsePragmaticTemporal(t => t.NormalizeInstantsToUtc = false)` — which the generated call
keeps.

Querying by a local day: convert the day's bounds in the business zone to UTC and compare instants —
never compare a stored UTC instant with a local wall time.

## Testing

```csharp
var clock = TestClock.AtNoon(2026, 5, 6);   // or new TestClock(instant); register as IClock
clock.AdvanceDays(3);
clock.SetBeforeRomeSpringForward(2026);

var calendar = new TemporalCalculator(new TestHolidayProvider().AddHoliday(2026, 12, 25, "IT"));
var context = TestTemporalContext.ForRome(now: instant);   // a request's zone, in a unit test
```

`TestHolidayProvider.AddHoliday(year, month, day, countryCode)` — the last argument is the **country**.
Freeze the clock in every test that crosses midnight, a month end or a DST change. See
`pragmatic-use-testing` for replacing `IClock` in the host under test.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Time off, Showcase and Invoicing
example applications — code that compiles and that `TimeOff.IntegrationTests`,
`Showcase.IntegrationTests` and `Invoicing.IntegrationTests` exercise — and kept identical to it by the
gate: a holiday provider and its wiring, business days counted, `[FromClock]`, `[FromBusinessTimezone]`,
and `TestClock` in a test.

`ZonedDateTime` arithmetic across a DST change is used by no tested application yet, so there is no
example of it here: the sections above are the reference.
