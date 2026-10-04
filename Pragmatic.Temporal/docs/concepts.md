# Architecture and Core Concepts

This guide explains **why** Pragmatic.Temporal exists, how its pieces fit together, and how to choose the right type for each situation. Read this before diving into the individual feature guides.

---

## The Problem

.NET ships `DateTime`, `DateTimeOffset`, and `TimeSpan`. All three conflate distinct temporal concepts into a single type, and the compiler cannot help you tell them apart.

### DateTime: one type, three meanings

```csharp
DateTime birthday = new DateTime(1990, 5, 15);        // Calendar date (no time)
DateTime meeting = new DateTime(2026, 3, 21, 14, 30, 0); // Wall clock time (no timezone)
DateTime logged = DateTime.UtcNow;                     // UTC instant
```

All three variables have the same type. The only hint is the `Kind` property (`Unspecified`, `Local`, or `Utc`), which is routinely ignored by serializers, ORMs, and comparison operators. Pass a `DateTime` across a method boundary and the receiver has no way to know which of the three meanings was intended.

### DateTimeOffset: offset without identity

```csharp
var rome = new DateTimeOffset(2026, 3, 21, 15, 0, 0, TimeSpan.FromHours(1));  // +01:00
var london = new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.FromHours(0)); // +00:00
```

Both values represent the same instant. But `+01:00` is shared by dozens of timezones (CET, WAT, etc.) -- the offset alone does not tell you which timezone produced it. After DST transitions, the offset changes even though the timezone does not. This makes it impossible to compute "same time tomorrow" correctly from a `DateTimeOffset` alone.

### Calendar arithmetic is broken

```csharp
// Adding 1 day across DST: DateTime thinks it's always 24 hours
var beforeDst = new DateTime(2026, 3, 29, 1, 0, 0); // 1:00 AM, day before spring forward
var nextDay = beforeDst.AddDays(1);                   // 1:00 AM next day -- but what UTC offset?
```

`DateTime.AddDays(1)` always adds exactly 24 hours. On the day clocks spring forward (in Europe/Rome, March 29 2026), the local day is only 23 hours long. The result lands on the wrong wall clock time. On fall back, the local day is 25 hours, and the same problem occurs in reverse.

### DateTime.Now kills testability

```csharp
public bool IsExpired() => DateTime.UtcNow > _expiryDate;
```

This line is impossible to test deterministically. You cannot set `DateTime.UtcNow` to a specific value. Every test that depends on the current time becomes flaky near midnight, near month boundaries, or during DST transitions.

---

## The Solution: Type = Scope

Pragmatic.Temporal provides types where the type itself defines the temporal scope. A `LocalDate` cannot accidentally carry time information. A `ZonedDateTime` cannot lose its timezone. The compiler enforces correct usage.

### The type hierarchy

```
                    LocalDate          Pure calendar date (birthday, deadline)
                    LocalTime          Time of day (store hours, alarm)
                    LocalDateTime      Date + time, no timezone (appointment)
                    ZonedDateTime      Instant + timezone (flight departure)
                    Duration           Physical elapsed time (always exact)
                    Period             Calendar elapsed time (1 month, 1 year)
                    DateRange          Closed date interval with set operations
                    CronExpression     Scheduling expression (cron syntax)
```

Each type wraps a .NET primitive (`DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`) but restricts the API surface so only semantically valid operations are available. You cannot compare a `LocalDate` with a `ZonedDateTime` -- they represent fundamentally different things.

### How types compose

```csharp
// Start with a calendar date
var date = new LocalDate(2026, 3, 21);

// Add a time of day to get a wall clock datetime
var appointment = date.At(new LocalTime(14, 30)); // LocalDateTime

// Place it in a timezone to get a specific instant
var flight = appointment.InZone("Europe/Rome");   // ZonedDateTime

// Convert to another timezone (same instant, different local time)
var inTokyo = flight.InZone("Asia/Tokyo");         // ZonedDateTime
```

The progression is always from less context to more context: `LocalDate` -> `LocalDateTime` -> `ZonedDateTime`. You can go backwards (`ZonedDateTime.Date` returns a `LocalDate`), but the compiler makes you acknowledge the timezone context explicitly.

---

## IClock: Testable Time

`IClock` is the abstraction that replaces `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, and `DateTimeOffset.UtcNow` throughout your application.

### The interface

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset Now { get; }
    DateOnly UtcToday { get; }
    DateOnly Today { get; }
    TimeOnly UtcTimeOfDay { get; }
    TimeOnly TimeOfDay { get; }
    TimeProvider GetTimeProvider();
}
```

`IClock` lives in `Pragmatic.Abstractions` (not in Pragmatic.Temporal itself), so any module in the ecosystem can depend on it without pulling in the full temporal package. The `GetTimeProvider()` method returns a .NET 8+ `TimeProvider` for interop with APIs that expect it (e.g., `Task.Delay`, `PeriodicTimer`, `IMemoryCache`).

### SystemClock (production)

`SystemClock` delegates to `TimeProvider.System`. It is registered as a singleton by default:

```csharp
services.AddSingleton<IClock, SystemClock>();
```

When using `Pragmatic.Composition`, this registration is automatic -- the SG detects `Pragmatic.Temporal` in your dependencies and registers `SystemClock` as the default `IClock`.

### TestClock (tests)

`TestClock` gives you full control over time in tests:

```csharp
var clock = new TestClock();
clock.SetDateTime(2026, 3, 21, 10, 0, 0);

// Time is now frozen at 2026-03-21 10:00:00 UTC
clock.UtcNow  // 2026-03-21T10:00:00Z

// Advance time manually
clock.Advance(TimeSpan.FromHours(2));
clock.UtcNow  // 2026-03-21T12:00:00Z

// Convenience: advance by days, hours
clock.AdvanceDays(1);
clock.AdvanceHours(3);
```

Static factory methods cover common scenarios:

```csharp
var clock = TestClock.AtNoon(2026, 3, 21);      // 12:00 UTC
var clock = TestClock.AtMidnight(2026, 1, 1);    // 00:00 UTC
var clock = TestClock.AtNow();                    // Current time, then frozen
```

For async tests where multiple reads of `UtcNow` must return different values (to avoid timestamp collisions), use auto-advance:

```csharp
var clock = TestClock.AtNoon(2026, 3, 21)
    .WithAutoAdvance(TimeSpan.FromMilliseconds(1));

// Each read advances by 1ms
clock.UtcNow  // T+0ms
clock.UtcNow  // T+1ms
clock.UtcNow  // T+2ms
```

### TimeProvider interop

`IClock.GetTimeProvider()` returns a `TimeProvider` that stays in sync with the clock. This means `TestClock` also controls `TimeProvider`-based APIs:

```csharp
// In DI registration
services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<IClock>().GetTimeProvider());
```

`AddPragmaticTemporal()` registers this automatically. When you replace the clock with `UseClock<T>()` or `UseClock(instance)`, the `TimeProvider` registration is updated to match.

---

## Core Types in Depth

### LocalDate

Calendar date without time or timezone. Backed by `DateOnly`.

**Use for**: birthdays, holidays, deadlines, reporting dates, invoice dates, anything where "March 21" is the complete information.

```csharp
var date = new LocalDate(2026, 3, 21);

// Properties
date.Year          // 2026
date.Month         // 3
date.Day           // 21
date.DayOfWeek     // DayOfWeek.Saturday
date.DayOfYear     // 80
date.Quarter       // 1
date.IsWeekend     // true
date.IsWeekday     // false
date.IsLeapYear    // false

// Arithmetic
date.AddDays(7)         // 2026-03-28
date.AddMonths(1)       // 2026-04-21
date.AddYears(1)        // 2027-03-21
date.DaysBetween(other) // signed integer

// Navigation
date.StartOfMonth()     // 2026-03-01
date.EndOfMonth()       // 2026-03-31
date.StartOfWeek()      // 2026-03-16 (Monday)
date.EndOfWeek()        // 2026-03-22 (Sunday)
date.StartOfQuarter()   // 2026-01-01
date.EndOfQuarter()     // 2026-03-31
date.StartOfYear()      // 2026-01-01
date.EndOfYear()        // 2026-12-31

// Compose with time
date.At(new LocalTime(14, 30))  // LocalDateTime
date.AtMidnight()               // LocalDateTime at 00:00
date.AtNoon()                   // LocalDateTime at 12:00

// Implicit conversion
DateOnly d = date;              // implicit operator
LocalDate ld = d;               // implicit operator
```

### LocalTime

Time of day without date or timezone. Backed by `TimeOnly`.

**Use for**: store hours, alarm times, recurring meeting times, any time that repeats across days.

```csharp
var time = new LocalTime(14, 30);

// Properties
time.Hour        // 14
time.Minute      // 30
time.Second      // 0
time.IsMorning   // false
time.IsAfternoon // true
time.IsEvening   // false

// Common times
LocalTime.Midnight   // 00:00:00
LocalTime.Noon       // 12:00:00

// Arithmetic
time.AddHours(2)     // 16:30
time.AddMinutes(15)  // 14:45
time.DurationUntil(other) // TimeSpan

// Checks
time.IsBetween(new LocalTime(9, 0), new LocalTime(17, 0)) // true

// Compose with date
time.On(new LocalDate(2026, 3, 21)) // LocalDateTime
```

### LocalDateTime

Date and time without timezone. Backed by `DateTime` with `Kind = Unspecified`.

**Use for**: recurring appointments, schedules, "meet at 2:30 PM" where the timezone is implied by context.

```csharp
var dt = new LocalDateTime(2026, 3, 21, 14, 30);

// Or compose from parts
var dt2 = new LocalDateTime(new LocalDate(2026, 3, 21), new LocalTime(14, 30));

// Decompose
var (date, time) = dt;  // Deconstruct

// Navigation
dt.StartOfDay()   // 2026-03-21T00:00:00
dt.EndOfDay()     // 2026-03-21T23:59:59.999
dt.StartOfHour()  // 2026-03-21T14:00:00

// Convert to zoned (this is where DST policies matter)
dt.InZone("Europe/Rome")  // ZonedDateTime (default: ShiftForward + UseStandardTime)
dt.InZone("Europe/Rome",
    nonExistentPolicy: NonExistentTimePolicy.ThrowException,
    ambiguousPolicy: AmbiguousTimePolicy.ThrowException)  // Strict mode
```

### ZonedDateTime

A specific instant in time with full timezone awareness. Backed by `DateTimeOffset` (UTC) + `TimeZoneInfo`.

**Use for**: flight departures, event timestamps, anything that happened or will happen at a specific moment on the planet.

```csharp
// From UTC (safest -- no DST ambiguity)
var rome = ZonedDateTime.FromUtc(DateTimeOffset.UtcNow, "Europe/Rome");

// From local time (requires DST policy)
var zoned = ZonedDateTime.FromLocal(
    new DateTime(2026, 3, 21, 14, 30, 0),
    TimeZoneResolver.GetTimeZone("Europe/Rome"));

// Strict mode (throws on DST edge cases)
var strict = ZonedDateTime.FromLocalStrict(
    new DateTime(2026, 3, 21, 14, 30, 0),
    TimeZoneResolver.GetTimeZone("Europe/Rome"));

// Current time in a zone (requires IClock)
var now = ZonedDateTime.Now("Europe/Rome", clock);

// Properties
rome.UtcDateTime           // DateTimeOffset (UTC)
rome.Zone                  // TimeZoneInfo
rome.ZoneId                // "Europe/Rome" (IANA)
rome.Offset                // TimeSpan (+01:00 or +02:00)
rome.LocalDateTime         // DateTime in that zone
rome.Date                  // LocalDate
rome.Time                  // LocalTime
rome.IsDaylightSavingTime  // bool

// Timezone conversion (same instant, different wall clock)
rome.InZone("Asia/Tokyo")  // Convert to Tokyo
rome.ToUtc()               // Convert to UTC DateTimeOffset

// Arithmetic
rome.AddDays(1)     // "Same time tomorrow" (calendar day, DST-safe)
rome.AddHours(1)    // Wall clock hour (may skip/repeat during DST)
rome.Add(Duration.FromHours(1))  // Physical hour (exactly 3600 seconds)
```

**Equality semantics**: Two `ZonedDateTime` values are equal if they represent the same UTC instant, regardless of timezone. Use `EqualsExact()` to compare both instant and timezone.

### Duration vs Period

These two types represent fundamentally different concepts:

| Aspect | Duration | Period |
|--------|----------|--------|
| Meaning | Physical elapsed time | Calendar elapsed time |
| Precision | Exact (ticks) | Years, months, days |
| "1 day" | Always 24 hours | Same wall clock time tomorrow |
| "1 month" | Not supported | Jan 31 + 1 month = Feb 28 |
| DST-safe | N/A (absolute) | Handled by type being operated on |
| Format | ISO 8601 `PT1H30M` | ISO 8601 `P1Y2M3D` |

```csharp
// Duration: exact elapsed time
var flight = Duration.FromHours(2) + Duration.FromMinutes(30);
flight.TotalMinutes  // 150.0

// Period: calendar arithmetic
var subscription = Period.FromYears(1);
var startDate = new LocalDate(2026, 3, 21);
var expiryDate = startDate.Add(subscription);  // 2027-03-21

// Period.Between computes the calendar difference
var age = Period.Between(
    new LocalDate(1990, 5, 15),
    new LocalDate(2026, 3, 21));
age.Years   // 35
age.Months  // 10
age.Days    // 6
```

### DateRange

Closed date range `[Start, End]` with set operations. Implements `IEnumerable<LocalDate>`.

```csharp
// Factory methods
var q1 = DateRange.Quarter(2026, 1);
var march = DateRange.Month(2026, 3);
var week = DateRange.Week(new LocalDate(2026, 3, 21));
var last30 = DateRange.LastDays(30);

// Properties
q1.Start       // 2026-01-01
q1.End         // 2026-03-31
q1.Days        // 90 (inclusive)
q1.IsSingleDay // false

// Set operations
q1.Contains(new LocalDate(2026, 2, 15))  // true
q1.Overlaps(march)                         // true
q1.Intersect(march)                        // [Mar 1 - Mar 31]
q1.Union(march)                            // [Jan 1 - Mar 31]

// Manipulation
march.Expand(5)   // [Feb 24 - Apr 5]
march.Shift(7)    // [Mar 8 - Apr 7]
march.Split(10)   // Chunks of 10 days

// Enumeration
foreach (var date in march.Weekdays()) { /* Mon-Fri only */ }
foreach (var date in march.Weekends()) { /* Sat-Sun only */ }
```

### CronExpression

Zero-dependency cron parser supporting standard 5-field and extended 6-field (seconds) formats.

```csharp
// Common expressions
CronExpression.EveryMinute   // "* * * * *"
CronExpression.Midnight      // "0 0 * * *"
CronExpression.Weekdays      // "0 0 * * 1-5"

// Factory methods
CronExpression.Daily(new TimeOnly(9, 0))                       // "0 9 * * *"
CronExpression.Weekly(DayOfWeek.Monday, new TimeOnly(8, 0))    // "0 8 * * 1"
CronExpression.Monthly(1, new TimeOnly(0, 0))                  // "0 0 1 * *"

// Custom parsing (supports *, ranges, lists, steps, L, W, #)
var cron = CronExpression.Parse("0/15 9-17 * * 1-5");

// Get occurrences
var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow);
var schedule = cron.GetOccurrences(from, until, zone);

// Check if a time matches
cron.Matches(DateTimeOffset.UtcNow)  // bool

// Properties
cron.Expression  // original string
cron.HasSeconds  // true for 6-field
cron.Semantics   // Unix (OR) or Quartz (AND) day matching
```

CronExpression is DST-aware: `GetNextOccurrence` accepts a `TimeZoneInfo` parameter and skips times that fall in DST gaps.

---

## DST Handling

Pragmatic.Temporal makes DST handling explicit. There are no silent defaults that paper over edge cases.

### The two DST problems

**Spring forward (gap)**: Clocks jump ahead. Some local times do not exist. In Europe/Rome on March 29, 2026, clocks jump from 02:00 to 03:00. The time 02:30 does not exist that day.

**Fall back (overlap)**: Clocks fall back. Some local times occur twice. In Europe/Rome on October 25, 2026, clocks fall from 03:00 back to 02:00. The time 02:30 occurs twice -- once at +02:00 (CEST) and once at +01:00 (CET).

### Policies

Two enums control behavior:

```csharp
public enum NonExistentTimePolicy
{
    ShiftForward,    // 02:30 -> 03:00 (first valid time after gap)
    ThrowException   // Throws NonExistentTimeException
}

public enum AmbiguousTimePolicy
{
    UseStandardTime,  // 02:30 +01:00 (later occurrence)
    UseDaylightTime,  // 02:30 +02:00 (earlier occurrence)
    ThrowException    // Throws AmbiguousTimeException
}
```

Every method that converts local time to zoned time accepts these policies as parameters:

```csharp
// Explicit policies on every conversion
var zoned = ZonedDateTime.FromLocal(localDateTime, zone,
    nonExistentPolicy: NonExistentTimePolicy.ShiftForward,
    ambiguousPolicy: AmbiguousTimePolicy.UseStandardTime);

// Strict mode: throw on any DST edge case
var strict = ZonedDateTime.FromLocalStrict(localDateTime, zone);
```

### TemporalContext and DST

`TemporalContext` provides conversion methods that apply configurable defaults:

```csharp
// ClientToUtc uses ShiftForward + UseStandardTime by default
var utc = context.ClientToUtc(localDateTime);

// Or specify policies explicitly
var utc = context.ClientToUtc(localDateTime,
    NonExistentTimePolicy.ThrowException,
    AmbiguousTimePolicy.ThrowException);
```

---

## Business Days

`ITemporalCalculator` provides business day calculations with pluggable holiday support.

### Without holidays

```csharp
var calculator = new TemporalCalculator();
var delivery = calculator.AddBusinessDays(today, 5); // Skips weekends only
var count = calculator.CountBusinessDays(from, to);  // O(1) formula
```

### With holidays

Plug in an `IHolidayProvider` for country-aware calculations:

```csharp
var holidays = StaticHolidayProvider.CreateBuilder()
    .AddHoliday("IT", 2026, 12, 25, "Christmas")
    .AddHoliday("IT", 2026, 12, 26, "St. Stephen's Day")
    .Build();

var calculator = new TemporalCalculator(holidays);
var delivery = calculator.AddBusinessDays(today, 5, "IT");
```

### IHolidayProvider

```csharp
public interface IHolidayProvider
{
    IEnumerable<string> SupportedCountries { get; }
    IEnumerable<Holiday> GetHolidays(int year, string countryCode);
    IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode);
    bool IsHoliday(LocalDate date, string countryCode);
}
```

Built-in providers:
- `NoHolidaysProvider` -- default, returns no holidays (singleton)
- `StaticHolidayProvider` -- configurable list with fluent builder

Holiday types: `Public`, `Regional`, `Bank`, `Optional`.

### Performance

`TemporalCalculator` uses O(1) mathematical formulas for counting business days (not iterative loops). For `AddBusinessDays`, full weeks are skipped in bulk (5 business days = 7 calendar days), with at most 4 iterations for the remainder.

---

## Timezone Resolution

`TimeZoneResolver` handles cross-platform timezone ID resolution. It accepts both IANA IDs (`Europe/Rome`) and Windows IDs (`Central European Standard Time`) and resolves them to `TimeZoneInfo`.

```csharp
// IANA ID (Linux native, Windows via conversion)
var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");

// Windows ID (Windows native, Linux via conversion)
var cet = TimeZoneResolver.GetTimeZone("Central European Standard Time");

// Get IANA ID from a TimeZoneInfo (cross-platform)
var ianaId = TimeZoneResolver.GetIanaId(zone); // "Europe/Rome"

// Validation
TimeZoneResolver.IsValidTimezone("Europe/Rome")  // true
TimeZoneResolver.IsValidTimezone("Invalid/Zone")  // false

// Common zones (cached)
var utc = TimeZoneResolver.CommonZones.Utc;
var ny = TimeZoneResolver.CommonZones.NewYork;
var tokyo = TimeZoneResolver.CommonZones.Tokyo;
```

The resolver includes OpenTelemetry tracing via `ActivitySource("Pragmatic.Temporal")` for observability of timezone resolution in production.

---

## TemporalContext: Per-Request Time

`TemporalContext` is a scoped service that holds the clock, client timezone, and business timezone for the current request. It provides conversion methods for the two most common scenarios: "show this to the user" (client timezone) and "calculate this for the business" (business timezone).

```csharp
public class TemporalContext
{
    public required TimeZoneInfo ClientTimeZone { get; init; }
    public required TimeZoneInfo BusinessTimeZone { get; init; }
    public required IClock Clock { get; init; }

    // Current time in different contexts
    public DateTimeOffset UtcNow { get; }
    public ZonedDateTime ClientNow { get; }
    public ZonedDateTime BusinessNow { get; }
    public LocalDate ClientToday { get; }
    public LocalDate BusinessToday { get; }
    public LocalDate UtcToday { get; }

    // Conversions
    public ZonedDateTime ToClientTime(DateTimeOffset utc);
    public ZonedDateTime ToBusinessTime(DateTimeOffset utc);
    public DateTimeOffset ClientToUtc(DateTime clientLocal);
    public DateTimeOffset BusinessToUtc(DateTime businessLocal);

    // Date range helpers for database queries
    public DateTimeOffset ClientStartOfDay(LocalDate date);
    public DateTimeOffset ClientEndOfDay(LocalDate date);
    public (DateTimeOffset Start, DateTimeOffset End) ClientTodayRange { get; }
    public (DateTimeOffset Start, DateTimeOffset End) BusinessTodayRange { get; }
}
```

### Factory methods

```csharp
// UTC context (both client and business = UTC)
var ctx = TemporalContext.Utc();

// Single zone context
var ctx = TemporalContext.ForZone("Europe/Rome");

// Custom context
var ctx = new TemporalContext
{
    Clock = clock,
    ClientTimeZone = TimeZoneResolver.GetTimeZone("America/New_York"),
    BusinessTimeZone = TimeZoneResolver.GetTimeZone("Europe/Rome")
};
```

### Date range queries

The `ClientStartOfDay` / `ClientEndOfDay` methods are designed for database queries that need UTC ranges corresponding to a client's local day:

```csharp
// "Show me today's orders" in the client's timezone
var (start, end) = context.ClientTodayRange;
var orders = await db.Orders
    .Where(o => o.CreatedAt >= start && o.CreatedAt < end)
    .ToListAsync();
```

---

## Relative Date Navigation

Extension methods on `LocalDate` provide relative navigation without manual arithmetic:

```csharp
var today = new LocalDate(2026, 3, 21); // Saturday

// Day-of-week navigation
today.Next(DayOfWeek.Monday)          // 2026-03-23
today.Previous(DayOfWeek.Friday)      // 2026-03-20
today.NextOrSame(DayOfWeek.Saturday)  // 2026-03-21 (same day)

// Month-based
today.FirstInMonth(DayOfWeek.Monday)   // 2026-03-02
today.LastInMonth(DayOfWeek.Friday)    // 2026-03-27
today.NthInMonth(2, DayOfWeek.Tuesday) // 2nd Tuesday = 2026-03-10
today.NthInMonth(-1, DayOfWeek.Sunday) // Last Sunday

// Year-based
today.FirstInYear(DayOfWeek.Monday)
today.LastInYear(DayOfWeek.Friday)

// Business day helpers
today.NextWeekday()       // Monday
today.PreviousWeekday()   // Friday
today.NearestWeekday()    // Sat->Fri, Sun->Mon

// ISO week
today.IsoWeekOfYear()     // ISO 8601 week number
today.IsoWeekYear()       // ISO week year (may differ near year boundary)
LocalDateExtensions.FromIsoWeekDate(2026, 12, DayOfWeek.Monday)
```

---

## ASP.NET Core Integration

The `Pragmatic.Temporal.AspNetCore` package provides middleware, timezone detection, and model binding.

### Timezone detection middleware

```csharp
app.UsePragmaticTemporal();
```

The middleware creates a `TemporalContext` per request with the detected client timezone. Detection strategies (evaluated in priority order):

| Strategy | Source | Priority |
|----------|--------|----------|
| `QueryStringTimeZoneStrategy` | `?tz=Europe/Rome` | 50 |
| `HeaderTimeZoneStrategy` | `X-Timezone: Europe/Rome` | 100 |
| `ClaimsTimeZoneStrategy` | JWT claim `timezone` | 200 |
| `CookieTimeZoneStrategy` | Cookie `tz` | 300 |

Lower priority wins. Implement `ITimeZoneDetectionStrategy` for custom detection.
Full guide: [ASP.NET Core Integration](aspnetcore.md).

### Model binding

`LocalDateModelBinder` binds `LocalDate` from query strings and route parameters using ISO 8601 format (`yyyy-MM-dd`).

---

## IPragmaticBuilder Integration

Configure temporal services in `Program.cs`:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseTemporal(temporal =>
    {
        temporal.UseHolidayProvider<ItalianHolidayProvider>();
        temporal.UseClock<SystemClock>();
    });
});
```

Default and business timezones are configured on `TemporalOptions` at registration time
(`AddPragmaticTemporal(o => o.DefaultTimeZone = ...)`; see [Getting Started](getting-started.md)).

### What gets registered automatically

`AddPragmaticTemporal()` registers the following defaults:

| Service | Default | Lifetime |
|---------|---------|----------|
| `IClock` | `SystemClock.Instance` | Singleton |
| `TimeProvider` | From `IClock.GetTimeProvider()` | Singleton |
| `IHolidayProvider` | `NoHolidaysProvider.Instance` | Singleton |
| `ITemporalCalculator` | `TemporalCalculator` (with IHolidayProvider) | Singleton |
| `TemporalContext` | Scoped factory using `TemporalOptions` | Scoped |
| `TemporalOptions` | UTC defaults | Singleton |

All registrations use `TryAdd`, so your overrides via `TemporalBuilder` always win.

---

## Ecosystem Integration

Pragmatic.Temporal is a foundational module. Other modules depend on `IClock` and the temporal types.

### Persistence

`Pragmatic.Temporal.EFCore` provides EF Core value converters for all temporal types: `LocalDate` maps to `date` columns, `ZonedDateTime` stores as an ISO 8601 string with zone id (`2024-01-15T10:30:00+01:00[Europe/Rome]`), and `Duration` stores as ticks (`long`). Full guide: [EF Core Integration](efcore.md).

### Events

Domain events use `IClock` (via `TimeProvider`) for their timestamps. When you use `TestClock`, event timestamps are deterministic.

### Actions

`DomainActionInvoker` uses `TimeProvider` for activity tracing timestamps. The clock flows through the entire pipeline.

### JSON Serialization

`Pragmatic.Temporal.Json` provides `System.Text.Json` converters for all temporal types, ensuring round-trip fidelity for API payloads. Full guide: [JSON Integration](json.md).

---

## Golden Rules

1. **Store UTC only** -- databases should contain UTC timestamps
2. **Convert at boundaries** -- timezone conversion at API input/output only
3. **Never query with local time** -- pre-calculate UTC ranges before querying
4. **Use IClock, never DateTime.Now** -- for testability
5. **Be explicit about DST** -- always specify a policy for ambiguous/non-existent times

---

## See Also

- [Getting Started](getting-started.md) -- Mental model, golden rules, minimal example
- [Core Types](core-types.md) -- All types, storage formats, conversion rules
- [DST Handling](dst-handling.md) -- Ambiguous times, non-existent times, explicit policies
- [Business Days](business-days.md) -- TemporalCalculator, IHolidayProvider, holiday types
- [Testing](testing.md) -- TestClock, TestTemporalContext, deterministic time
- [JSON Integration](json.md) -- System.Text.Json converters, wire formats, Minimal API setup
- [EF Core Integration](efcore.md) -- Value converters, column mappings, query extensions
- [ASP.NET Core Integration](aspnetcore.md) -- Middleware, timezone detection, model binding
- [Common Mistakes](common-mistakes.md) -- Wrong/Right/Why for the most frequent issues
- [Troubleshooting](troubleshooting.md) -- Problem/Checklist format for runtime issues
