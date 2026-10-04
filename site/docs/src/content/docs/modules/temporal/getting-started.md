---
title: "Getting Started with Pragmatic.Temporal"
description: "This guide takes you from zero to your first date-safe, testable code in a few minutes. For the mental"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/getting-started.md
sidebar:
  order: 2
---
This guide takes you from zero to your first date-safe, testable code in a few minutes. For the mental
model behind the library (type = scope, `Duration` vs `Period`, DST policies), read
[Concepts](/modules/temporal/concepts/); this page is the hands-on path.

## Installation

```bash
# Core package (types, arithmetic, business days, cron, IClock)
dotnet add package Pragmatic.Temporal

# Optional integrations
dotnet add package Pragmatic.Temporal.Json        # System.Text.Json converters
dotnet add package Pragmatic.Temporal.EFCore      # EF Core value converters + conventions
dotnet add package Pragmatic.Temporal.AspNetCore  # Middleware, timezone detection, model binding
dotnet add package Pragmatic.Temporal.Testing     # TestClock and test utilities
```

## Your First Temporal Types

**When you need this**: any time you are about to write `DateTime` and the value is *not* a precise
instant: a birthday, a store opening time, an appointment on somebody's calendar.

**What you write**:

```csharp
using Pragmatic.Temporal.Types;

var orderDate    = new LocalDate(2026, 6, 15);            // date only: no time, no zone
var deliveryTime = new LocalTime(14, 30);                 // time only: no date, no zone
var appointment  = orderDate.At(deliveryTime);            // LocalDateTime: wall clock, no zone

// Only when you need a real instant on the planet, bring in a timezone:
var flight = ZonedDateTime.FromUtc(DateTimeOffset.UtcNow, "Europe/Rome");
```

**What you get**: the compiler now enforces the temporal scope. A `LocalDate` cannot accidentally
carry a time; a `ZonedDateTime` cannot lose its zone; you can no longer pass "a date" where "an
instant" is required. The `Kind` ambiguity of `DateTime` is gone.

| Type | Use case | Example |
|------|----------|---------|
| `LocalDate` | Calendar date without time | Birth dates, holidays, deadlines |
| `LocalTime` | Time of day without date | Store hours, meeting times |
| `LocalDateTime` | Date + time without timezone | Wall clock appointments |
| `ZonedDateTime` | Instant + timezone | Flight times, global events |
| `Duration` | Physical elapsed time | Processing time, timeouts |
| `Period` | Calendar elapsed time | "1 month", subscription terms |

## Date Arithmetic

**When you need this**: "next month", "in 7 days", "how many days until", the places where raw
`DateTime` math silently produces the wrong answer (`Jan 31 + 1 month`?).

**What you write**:

```csharp
var date = new LocalDate(2026, 1, 31);

var nextWeek  = date.AddDays(7);       // 2026-02-07
var nextMonth = date.AddMonths(1);     // 2026-02-28, clamped, calendar-correct
var nextYear  = date.AddYears(1);      // 2027-01-31

var checkIn  = new LocalDate(2026, 6, 1);
var checkOut = new LocalDate(2026, 6, 4);
var nights   = checkOut.DaysBetween(checkIn);   // 3, signed: checkOut − checkIn

// Navigation helpers
date.StartOfMonth();   // 2026-01-01
date.EndOfMonth();     // 2026-01-31
date.StartOfWeek();    // Monday of that week (ISO 8601 default)
```

**What you get**: overflow-safe month arithmetic (Jan 31 + 1 month = Feb 28, not an exception, not
March 3), and navigation methods that replace hand-rolled `new DateTime(y, m, 1).AddMonths(1).AddDays(-1)`
gymnastics.

## Testable Time: Inject IClock

**When you need this**: everywhere you would write `DateTime.Now`, `DateTime.UtcNow`, or
`DateTimeOffset.UtcNow`. Direct calls make every time-dependent test flaky.

**What you write**:

```csharp
using Pragmatic.Temporal.Clock;

public class BookingService(IClock clock)
{
    public bool IsPast(LocalDate date) => date < clock.Today;
    public DateTimeOffset Timestamp() => clock.UtcNow;
}
```

And in tests (package `Pragmatic.Temporal.Testing`):

```csharp
var clock = new TestClock();
clock.SetDateTime(2026, 6, 15, 10, 0, 0);   // frozen at 2026-06-15 10:00 UTC

var service = new BookingService(clock);
// ... assert deterministically ...

clock.Advance(TimeSpan.FromHours(2));        // move time forward when the test needs it
```

**What you get**: deterministic tests, with no flakiness near midnight, month boundaries, or DST
transitions. `IClock` also exposes `Now`, `Today`/`UtcToday`, `TimeOfDay`/`UtcTimeOfDay`, and
`GetTimeProvider()` for interop with `TimeProvider`-based APIs. See [Testing](/modules/temporal/testing/).

## Registering the Services

**When you need this**: wiring the module into your application host.

**What you write**:

```csharp
using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Timezone;

services.AddPragmaticTemporal(o =>
{
    o.DefaultTimeZone  = TimeZoneResolver.GetTimeZone("Europe/Rome");
    o.BusinessTimeZone = TimeZoneResolver.GetTimeZone("Europe/Rome");
});
```

**What you get**: `IClock` (→ `SystemClock`), a synced `TimeProvider`, `IHolidayProvider`
(→ no holidays by default), `ITemporalCalculator`, and a scoped `TemporalContext` built from the
options above. All registrations use `TryAdd`, so anything you register beforehand wins.

To swap implementations there are dedicated helpers:

```csharp
services.AddPragmaticTemporal()
        .UseClock(new TestClock())                 // also re-syncs TimeProvider
        .UseHolidayProvider<MyHolidayProvider>();
```

> **Note**: in a `Pragmatic.Composition` host, `AddPragmaticTemporal()` is registered automatically
> when the source generator detects the package; you only add the call yourself in a plain host.

## Business Days

**When you need this**: delivery estimates, SLA deadlines, payment terms, anything that skips
weekends and (optionally) public holidays.

**What you write**:

```csharp
using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Holidays;

// Weekend-only (no holidays)
var calculator = new TemporalCalculator();
var delivery = calculator.AddBusinessDays(new LocalDate(2026, 6, 15), 5);

// With country-aware holidays
var holidays = StaticHolidayProvider.CreateBuilder()
    .AddHoliday("IT", 2026, 12, 25, "Christmas")
    .AddHoliday("IT", 2026, 12, 26, "St. Stephen's Day")
    .Build();

var itCalculator = new TemporalCalculator(holidays);
var itDelivery = itCalculator.AddBusinessDays(new LocalDate(2026, 12, 23), 3, "IT");

var count = itCalculator.CountBusinessDays(
    new LocalDate(2026, 12, 21), new LocalDate(2026, 12, 28), "IT");  // [from, to): end exclusive
var next  = itCalculator.NextBusinessDay(new LocalDate(2026, 12, 24), "IT");
```

In DI, register the provider once and inject `ITemporalCalculator`:

```csharp
services.AddPragmaticTemporal(holidays);   // overload taking an IHolidayProvider
```

**What you get**: O(1) business-day math (no day-by-day loops), holiday awareness keyed by country
code, and `IsBusinessDay` / `IsHoliday` / `NextBusinessDay` / `PreviousBusinessDay` helpers.
Full reference: [Business Days](/modules/temporal/business-days/).

## Scheduling with Cron

**When you need this**: recurring schedules, such as "every weekday at 9", "first of the month".

**What you write**:

```csharp
var daily  = CronExpression.Daily(new TimeOnly(9, 0));                    // "0 9 * * *"
var weekly = CronExpression.Weekly(DayOfWeek.Monday, new TimeOnly(8, 0)); // "0 8 * * 1"
var custom = CronExpression.Parse("0/15 9-17 * * 1-5");                   // every 15 min, business hours

DateTimeOffset? nextRun = daily.GetNextOccurrence(clock.UtcNow);
```

**What you get**: zero-dependency cron parsing (standard 5-field plus seconds, `L`/`W`/`#` extensions),
DST-aware occurrence calculation, and lazy `GetOccurrences(from, until)` enumeration. Details in
[Core Types](/modules/temporal/core-types/).

## Golden Rules

1. **Store UTC only**: databases contain UTC instants; wall-clock types (`LocalDate`, `LocalTime`) for calendar data.
2. **Convert at boundaries**: timezone conversion happens at API input/output, never in business logic or queries.
3. **Never query with local time**: pre-calculate UTC ranges, then query.
4. **Use `IClock`, never `DateTime.Now`**: the analyzers (PRAG0900+) will remind you.
5. **Be explicit about DST**: conversions from local time take policies for non-existent/ambiguous times.

## Next Steps

- [Concepts](/modules/temporal/concepts/): the mental model (type = scope, `Duration` vs `Period`, `TemporalContext`)
- [Core Types](/modules/temporal/core-types/): full reference for every type
- [Business Days](/modules/temporal/business-days/): holiday providers and calculator in depth
- [DST Handling](/modules/temporal/dst-handling/): ambiguous and non-existent times, policies
- [Testing](/modules/temporal/testing/): `TestClock`, `TestTemporalContext`, `TestHolidayProvider`
- [Common Mistakes](/modules/temporal/common-mistakes/): the most frequent pitfalls, wrong/right/why
