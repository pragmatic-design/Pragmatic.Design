---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Temporal. Each section shows the wrong approach, the correct approach, and explains w"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/common-mistakes.md
sidebar:
  order: 10
---
These are the most common issues developers encounter when using Pragmatic.Temporal. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Using DateTime.Now Instead of IClock

**Wrong:**

```csharp
public class InvoiceService
{
    public Invoice CreateInvoice(Order order)
    {
        return new Invoice
        {
            CreatedAt = DateTime.UtcNow,
            DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
        };
    }
}
```

**Compile result:** Analyzer `PRAG0900` fires a warning: "Use IClock instead of DateTime.UtcNow" and `PRAG0901` fires: "Use IClock instead of DateTime.Today".

**Right:**

```csharp
public class InvoiceService(IClock clock)
{
    public Invoice CreateInvoice(Order order)
    {
        return new Invoice
        {
            CreatedAt = clock.UtcNow,
            DueDate = clock.UtcToday.AddDays(30)
        };
    }
}
```

**Why:** `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, and `DateTimeOffset.UtcNow` are all non-deterministic. Tests that depend on them become flaky near midnight, near month/year boundaries, or during DST transitions. `IClock` is injectable and replaceable with `TestClock` for deterministic testing. The analyzers (PRAG0900, PRAG0901) detect these usages at compile time.

---

## 2. Ignoring DST When Converting Local Time to UTC

**Wrong:**

```csharp
public DateTimeOffset ScheduleMeeting(int year, int month, int day, int hour, int minute, string timezone)
{
    var local = new DateTime(year, month, day, hour, minute, 0);
    var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
    var offset = zone.GetUtcOffset(local);
    return new DateTimeOffset(local, offset).ToUniversalTime();
}
```

**Runtime result:** On March 29, 2026 in Europe/Rome, scheduling a meeting at 02:30 produces an incorrect result. The time 02:30 does not exist (clocks jump from 02:00 to 03:00). `GetUtcOffset` returns the standard offset (+01:00), creating a `DateTimeOffset` that corresponds to a moment during the DST gap -- an instant that never occurs on any wall clock in Rome.

**Right:**

```csharp
public DateTimeOffset ScheduleMeeting(int year, int month, int day, int hour, int minute, string timezone)
{
    var local = new LocalDateTime(year, month, day, hour, minute);
    var zone = TimeZoneResolver.GetTimeZone(timezone);
    var zoned = ZonedDateTime.FromLocal(local.ToDateTime(), zone,
        nonExistentPolicy: NonExistentTimePolicy.ShiftForward,
        ambiguousPolicy: AmbiguousTimePolicy.UseStandardTime);
    return zoned.ToUtc();
}
```

**Why:** `ZonedDateTime.FromLocal` checks for DST gaps (non-existent times) and overlaps (ambiguous times) before constructing the value. The policies make your intent explicit: `ShiftForward` moves 02:30 to 03:00 (the first valid time after the gap), while `ThrowException` forces the caller to handle the edge case. Without these checks, you silently produce timestamps that do not correspond to any real wall clock time.

---

## 3. Using Duration for Calendar Arithmetic

**Wrong:**

```csharp
// "Deliver in 5 business days" - using Duration
var deliveryTime = Duration.FromDays(5);
var deliveryDate = ZonedDateTime.FromUtc(clock.UtcNow, "Europe/Rome")
    .Add(deliveryTime); // Adds exactly 120 hours
```

**Runtime result:** `Duration.FromDays(5)` is always exactly 120 hours. Over a DST transition, this may land on a different wall clock time than expected. More importantly, "5 days" in business context typically means "5 business days" (excluding weekends and holidays), not 120 hours.

**Right:**

```csharp
// Calendar days (DST-safe)
var date = new LocalDate(2026, 3, 21);
var fiveDaysLater = date.AddDays(5); // Always March 26, regardless of DST

// Business days (weekends and holidays excluded)
var calculator = new TemporalCalculator(holidayProvider);
var deliveryDate = calculator.AddBusinessDays(date, 5, "IT");
```

**Why:** `Duration` represents physical elapsed time and is appropriate for measuring intervals between instants (e.g., "this request took 150ms"). For "5 days from now" in calendar terms, use `LocalDate.AddDays()` or `ZonedDateTime.AddDays()` which operate on wall clock time. For business logic, use `ITemporalCalculator.AddBusinessDays()` which skips weekends and holidays.

---

## 4. Comparing DateTimeOffset with Relational Operators

**Wrong:**

```csharp
var rome = new DateTimeOffset(2026, 3, 21, 15, 0, 0, TimeSpan.FromHours(1));
var london = new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.FromHours(0));

if (rome > london) // Same instant! But comparison depends on implementation
{
    Console.WriteLine("Rome is later");
}
```

**Compile result:** Analyzer `PRAG0903` fires a warning: "DateTimeOffset relational comparison without .UtcDateTime -- consider comparing UTC instants explicitly."

**Right:**

```csharp
var rome = new DateTimeOffset(2026, 3, 21, 15, 0, 0, TimeSpan.FromHours(1));
var london = new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.FromHours(0));

// Option 1: Compare UTC instants directly
if (rome.UtcDateTime > london.UtcDateTime) { /* ... */ }

// Option 2: Use ZonedDateTime (comparison is always UTC-based)
var romeZoned = ZonedDateTime.FromUtc(rome, "Europe/Rome");
var londonZoned = ZonedDateTime.FromUtc(london, "Europe/London");

if (romeZoned > londonZoned) { /* never ambiguous */ }
```

**Why:** `DateTimeOffset` comparison operators compare UTC instants, which is correct -- but the intent is not obvious when reading the code. Two `DateTimeOffset` values with different offsets that represent the same instant will be equal, which surprises developers expecting local-time comparison. `ZonedDateTime` comparison is always UTC-based and makes this explicit. The analyzer nudges you toward `.UtcDateTime` for clarity.

---

## 5. Querying the Database with Local Time

**Wrong:**

```csharp
// "Show me today's orders" for a user in Rome
var today = DateTime.Today; // Server's local date!
var orders = await db.Orders
    .Where(o => o.CreatedAt.Date == today)
    .ToListAsync();
```

**Runtime result:** `DateTime.Today` returns the server's local date, not the user's. If the server is in UTC and the user is in Rome (+01:00 or +02:00), orders created after 11 PM Rome time appear as "tomorrow" in UTC. The query misses late-night orders and includes early-morning orders from the next Rome day.

**Right:**

```csharp
// Use TemporalContext for timezone-aware date ranges
var (start, end) = context.ClientTodayRange;
var orders = await db.Orders
    .Where(o => o.CreatedAt >= start && o.CreatedAt < end)
    .ToListAsync();
```

Or manually:

```csharp
var clientToday = context.ClientToday;
var startUtc = context.ClientStartOfDay(clientToday);
var endUtc = context.ClientEndOfDay(clientToday);

var orders = await db.Orders
    .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
    .ToListAsync();
```

**Why:** "Today" depends on the timezone. `TemporalContext.ClientStartOfDay` converts the client's midnight to a UTC `DateTimeOffset`, accounting for DST. The query uses a UTC range, which the database can evaluate efficiently against UTC-stored timestamps. Never compare `.Date` in database queries -- it forces client-side evaluation and ignores timezone offsets.

---

## 6. Creating DateTime Without Specifying Kind

**Wrong:**

```csharp
var timestamp = new DateTime(2026, 3, 21, 14, 30, 0);
// timestamp.Kind == DateTimeKind.Unspecified
```

**Compile result:** Analyzer `PRAG0902` fires a warning: "new DateTime() without DateTimeKind -- Kind is Unspecified and may cause incorrect conversions."

**Right:**

```csharp
// Option 1: Use the appropriate Pragmatic type
var local = new LocalDateTime(2026, 3, 21, 14, 30);       // Wall clock time
var date = new LocalDate(2026, 3, 21);                     // Calendar date

// Option 2: If you must use DateTime, specify Kind
var utc = new DateTime(2026, 3, 21, 14, 30, 0, DateTimeKind.Utc);
```

**Why:** `DateTimeKind.Unspecified` is the default, and it causes silent bugs. `TimeZoneInfo.ConvertTimeToUtc()` assumes `Unspecified` means local, but `DateTimeOffset` constructors reject it. Different APIs interpret `Unspecified` differently, leading to off-by-hours errors. Pragmatic types eliminate the ambiguity: `LocalDateTime` is always wall clock time (no timezone), `ZonedDateTime` is always a specific instant.

---

## 7. Forgetting Country Code in Business Day Calculations

**Wrong:**

```csharp
var calculator = new TemporalCalculator(italianHolidays);

// Friday Dec 24 -- add 1 business day
var result = calculator.AddBusinessDays(new LocalDate(2026, 12, 24), 1);
// Result: Monday Dec 28 -- skipped weekend but NOT Christmas
```

**Runtime result:** `AddBusinessDays(from, days)` without a country code skips weekends only. Even though the `TemporalCalculator` was constructed with an `IHolidayProvider`, the no-country overload does not consult it. Christmas Day (Dec 25) is treated as a business day.

**Right:**

```csharp
var calculator = new TemporalCalculator(italianHolidays);

// Add 1 business day, skipping weekends AND Italian holidays
var result = calculator.AddBusinessDays(new LocalDate(2026, 12, 24), 1, "IT");
// Result: Monday Dec 29 -- skipped Dec 25 (Christmas) and Dec 26 (St. Stephen's)
```

**Why:** `ITemporalCalculator` has three overloads for `AddBusinessDays`:
1. `AddBusinessDays(from, days)` -- weekends only
2. `AddBusinessDays(from, days, countryCode)` -- weekends + holidays from provider
3. `AddBusinessDays(from, days, holidays)` -- weekends + explicit holiday list

The overload without a country code is intentionally holiday-unaware for cases where you only care about weekends. If your business logic requires holiday awareness, always pass the country code.

---

## 8. Using Windows Timezone IDs in Cross-Platform Code

**Wrong:**

```csharp
var zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
```

**Runtime result:** Works on Windows. Throws `TimeZoneNotFoundException` on Linux and macOS, where timezones use IANA identifiers.

**Right:**

```csharp
// Use TimeZoneResolver -- handles both IANA and Windows IDs on any platform
var zone = TimeZoneResolver.GetTimeZone("Europe/Rome");

// Or if you receive a Windows ID from an external system
var zone = TimeZoneResolver.GetTimeZone("Central European Standard Time"); // Works everywhere
```

**Why:** `TimeZoneResolver.GetTimeZone` first tries the ID directly, then attempts IANA-to-Windows and Windows-to-IANA conversions. It works on any platform regardless of which ID format you provide. Always use IANA IDs (`Europe/Rome`, `America/New_York`) in your code and configuration -- they are the universal standard.

---

## 9. Storing DateTimeOffset with Preserved Offset in the Database

**Wrong:**

```csharp
public class Event
{
    // Stored as datetimeoffset(7) in SQL Server
    public DateTimeOffset StartsAt { get; set; }
}

// Different offsets for the same instant depending on when it was stored
var summer = new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.FromHours(2));  // CEST
var winter = new DateTimeOffset(2026, 1, 15, 14, 0, 0, TimeSpan.FromHours(1));  // CET
```

**Runtime result:** The database preserves the offset, so queries like `WHERE StartsAt >= '2026-07-15 12:00:00+00:00'` work correctly for instant comparison. But grouping by date (`StartsAt.Date`) produces inconsistent results because the local date component depends on the stored offset, which varies by season.

**Right:**

```csharp
public class Event
{
    // Store as UTC always
    public DateTimeOffset StartsAtUtc { get; set; }

    // Store timezone separately if you need to reconstruct the original local time
    public string TimeZoneId { get; set; }
}

// Always convert to UTC before storing
entity.StartsAtUtc = zonedDateTime.ToUtc();
entity.TimeZoneId = zonedDateTime.ZoneId;

// Reconstruct on read
var zoned = ZonedDateTime.FromUtc(entity.StartsAtUtc, entity.TimeZoneId);
```

**Why:** Storing UTC normalizes all timestamps to a single reference frame. Date grouping, range queries, and ordering all work correctly without offset arithmetic. If you need to display the original local time, store the timezone ID separately -- unlike the offset, the timezone ID is stable across DST transitions and lets you reconstruct the correct local time for any date.

---

## 10. Mixing AddDays (Calendar) and Add(Duration) on ZonedDateTime

**Wrong:**

```csharp
var meeting = ZonedDateTime.FromUtc(clock.UtcNow, "Europe/Rome");

// "Same meeting, 24 hours later"
var nextDay = meeting.Add(Duration.OneDay);

// On spring forward day: meeting at 14:00 + 24h = 15:00 (not 14:00!)
// On fall back day: meeting at 14:00 + 24h = 13:00 (not 14:00!)
```

**Runtime result:** `Add(Duration.OneDay)` adds exactly 24 physical hours. When DST changes the UTC offset, the local wall clock time shifts. If the meeting was at 14:00 Rome time before spring forward, adding 24 physical hours lands at 15:00 Rome time the next day.

**Right:**

```csharp
var meeting = ZonedDateTime.FromUtc(clock.UtcNow, "Europe/Rome");

// "Same meeting time tomorrow" (wall clock)
var nextDay = meeting.AddDays(1);
// Always 14:00 Rome time, regardless of DST

// "Exactly 24 hours later" (physical time)
var later = meeting.Add(Duration.OneDay);
// May be different wall clock time during DST transitions
```

**Why:** `ZonedDateTime.AddDays(1)` means "same local time tomorrow" -- it computes the new local date, then resolves it in the timezone with DST policies. `ZonedDateTime.Add(Duration)` means "this many seconds later" -- it operates on the UTC instant. Choose based on intent: recurring meetings use `AddDays`, timeouts and SLAs use `Add(Duration)`.

---

## 11. Not Registering TemporalContext When Using It Outside ASP.NET Core

**Wrong:**

```csharp
// In a background service or console app
public class ReportGenerator(TemporalContext context)
{
    // InvalidOperationException: No service for type 'TemporalContext' has been registered
}
```

**Runtime result:** `TemporalContext` is registered by `AddPragmaticTemporal()`, but if you skip that call (or use a different DI container for background workers), the scoped factory is not available.

**Right:**

```csharp
// Option 1: Register temporal services
services.AddPragmaticTemporal(options =>
{
    options.DefaultTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    options.BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
});

// Option 2: Create a context manually for background work
var context = TemporalContext.ForZone("Europe/Rome", clock);

// Option 3: Use UTC context when timezone does not matter
var context = TemporalContext.Utc(clock);
```

**Why:** `TemporalContext` is a scoped service designed for per-request use in ASP.NET Core. For background services, console apps, or test code, use the static factory methods (`TemporalContext.Utc()`, `TemporalContext.ForZone()`) to create instances directly without DI.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| `DateTime.Now` / `.UtcNow` | `PRAG0900` warning |
| `DateTime.Today` | `PRAG0901` warning |
| `new DateTime()` without Kind | `PRAG0902` warning |
| `DateTimeOffset` relational comparison | `PRAG0903` warning |
| `DateTime.Now` in test code | `PRAG0904` info |
| DST gap ignored during conversion | Incorrect UTC timestamp, off by 1 hour |
| `Duration.FromDays()` for calendar arithmetic | Wrong wall clock time after DST |
| Querying with local time | Missing or extra records near day boundary |
| Missing country code in business days | Holidays treated as business days |
| Windows timezone ID on Linux | `TimeZoneNotFoundException` at runtime |
| Storing offset instead of UTC | Inconsistent date grouping in queries |
| `Add(Duration)` vs `AddDays()` confusion | Wrong local time after DST transition |
| Missing `AddPragmaticTemporal()` | `InvalidOperationException` for TemporalContext |
