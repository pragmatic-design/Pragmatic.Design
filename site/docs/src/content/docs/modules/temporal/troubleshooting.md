---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Temporal. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/troubleshooting.md
sidebar:
  order: 11
---
Practical problem/solution guide for Pragmatic.Temporal. Each section covers a common issue, the likely causes, and the fix.

---

## Incorrect Time After Timezone Conversion

A `DateTimeOffset` or `ZonedDateTime` shows the wrong local time after converting between timezones.

### Checklist

1. **Is the source value in UTC?** The safest conversion path is always UTC -> target zone. If the source has a non-UTC offset, convert to UTC first:

   ```csharp
   var utc = sourceValue.ToUniversalTime();
   var target = ZonedDateTime.FromUtc(utc, "Europe/Rome");
   ```

2. **Is the timezone ID correct?** `"CET"` is not a valid IANA timezone (it is an abbreviation). Use `"Europe/Rome"`, `"Europe/Berlin"`, etc. Validate with:

   ```csharp
   TimeZoneResolver.IsValidTimezone("CET")          // false
   TimeZoneResolver.IsValidTimezone("Europe/Rome")   // true
   ```

3. **Is DST being accounted for?** If you use `TimeZoneInfo.GetUtcOffset()` directly, the offset changes between standard and daylight time. `ZonedDateTime` handles this automatically.

4. **Are you comparing local times across timezones?** Two `DateTime` values from different timezones cannot be meaningfully compared. Convert both to UTC first or use `ZonedDateTime` (which compares UTC instants).

---

## TimeZoneNotFoundException at Runtime

`TimeZoneResolver.GetTimeZone()` throws `TimeZoneNotFoundException`.

### Checklist

1. **Check the timezone ID format.** Use IANA IDs (`Europe/Rome`) for portability. Windows IDs (`Central European Standard Time`) work but are not available on Linux/macOS natively.

2. **Check for typos.** Common mistakes: `Europe/Roma` (should be `Europe/Rome`), `US/Eastern` (should be `America/New_York`).

3. **Use TryGetTimeZone for user input.** If the timezone ID comes from a client or external system, use the Try pattern:

   ```csharp
   if (!TimeZoneResolver.TryGetTimeZone(userInput, out var zone))
   {
       // Handle invalid timezone -- fall back to default
       zone = TimeZoneInfo.Utc;
   }
   ```

4. **Check the platform.** On Linux containers, timezone data comes from the OS `tzdata` package — see [Timezone data on Linux containers](#timezone-data-on-linux-containers) below.

5. **Check TemporalOptions.ThrowOnInvalidTimeZone.** When set to `false` (default), invalid timezone IDs fall back to `DefaultTimeZone` with a logged warning instead of throwing.

---

## Timezone Data on Linux Containers

Pragmatic.Temporal resolves zones through the operating system's `TimeZoneInfo` (unlike
NodaTime, which embeds its own tzdb copy). This keeps the library dependency-free, but it
means the OS must ship timezone data.

**Symptom:** every `TimeZoneResolver.GetTimeZone("Europe/Rome")` call throws
`TimeZoneNotFoundException` in the container while the same code works on your machine.

**Cause:** slim images (Alpine, distroless, some `-slim` variants) do not include the
`tzdata` package, so the OS has no IANA database to resolve against.

**Fix — install `tzdata` in the image:**

```dockerfile
# Debian/Ubuntu-based images (e.g. mcr.microsoft.com/dotnet/aspnet)
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*

# Alpine-based images
RUN apk add --no-cache tzdata
```

For distroless images, copy `/usr/share/zoneinfo` from a build stage or switch to a base
image that includes it. .NET also honors the `TZ` environment variable for the *local*
zone, but named-zone lookups always need the zoneinfo files.

**Keep it updated:** timezone rules change several times a year (governments move DST
dates at short notice). Because the data comes from the OS, you pick up new rules by
rebuilding the image against an updated base — not by updating a NuGet package. If your
app does date math far in the future across political timezone changes, schedule regular
base-image rebuilds.

---

## Business Day Calculation Returns Unexpected Date

`AddBusinessDays` or `CountBusinessDays` returns a date that does not match expectations.

### Checklist

1. **Did you pass a country code?** Without a country code, only weekends are excluded. Holidays are only considered when you call the `(from, days, countryCode)` overload.

2. **Is the holiday provider registered?** Check that `IHolidayProvider` is registered in DI and that it contains holidays for the correct year and country:

   ```csharp
   var holidays = provider.GetHolidays(2026, "IT");
   // Verify Christmas, St. Stephen's, etc. are present
   ```

3. **Is the holiday in the correct year?** `StaticHolidayProvider` stores holidays by `(year, countryCode)`. If you added holidays for 2025 but are querying for 2026, they will not be found.

4. **Is the direction correct?** `AddBusinessDays(date, -5)` subtracts business days (goes backward). Verify the sign of the `days` parameter.

5. **Is `CountBusinessDays` using the expected range?** The range is `[from, to)` -- `from` is inclusive, `to` is exclusive. If `from >= to`, the result is 0.

---

## TestClock Not Affecting Other Services

You set up a `TestClock` in tests, but services still use the real system time.

### Checklist

1. **Did you register the TestClock as IClock in DI?**

   ```csharp
   var clock = new TestClock();
   services.AddSingleton<IClock>(clock);
   ```

2. **Did you register it before building the service provider?** `TryAddSingleton` (used by `AddPragmaticTemporal`) does not replace an existing registration. Register your `TestClock` first, or use `UseClock()`:

   ```csharp
   services.AddPragmaticTemporal();
   services.UseClock(clock); // Replaces the default SystemClock
   ```

3. **Is TimeProvider also updated?** Some .NET APIs use `TimeProvider` instead of `IClock`. `UseClock()` updates both registrations. If you registered `IClock` manually, also register `TimeProvider`:

   ```csharp
   services.AddSingleton<IClock>(clock);
   services.AddSingleton<TimeProvider>(clock.GetTimeProvider());
   ```

4. **Are you using the clock from the same DI scope?** `TemporalContext` is scoped. In integration tests, ensure the test clock is resolved from the same scope as the context.

---

## DST Gap or Overlap Exception in Production

`NonExistentTimeException` or `AmbiguousTimeException` thrown in production when converting user input.

### Checklist

1. **Are you using ThrowException policies?** Check if `ZonedDateTime.FromLocal()` or `ZonedDateTime.FromLocalStrict()` is being called with `ThrowException` policies. For user-facing input, prefer `ShiftForward` / `UseStandardTime`:

   ```csharp
   var zoned = ZonedDateTime.FromLocal(localDateTime, zone,
       nonExistentPolicy: NonExistentTimePolicy.ShiftForward,
       ambiguousPolicy: AmbiguousTimePolicy.UseStandardTime);
   ```

2. **Are you validating user input before conversion?** Check if the user submitted a time that falls in a DST gap:

   ```csharp
   var zone = TimeZoneResolver.GetTimeZone(timezoneId);
   var dt = DateTime.SpecifyKind(userDateTime, DateTimeKind.Unspecified);

   if (zone.IsInvalidTime(dt))
       return Error("This time does not exist due to daylight saving.");

   if (zone.IsAmbiguousTime(dt))
       return Error("This time is ambiguous. Please specify AM/PM or offset.");
   ```

3. **Check TemporalOptions defaults.** `TemporalOptions.NonExistentTimeHandling` and `AmbiguousTimeHandling` provide application-wide defaults. Set them to safe values:

   ```csharp
   services.AddPragmaticTemporal(o =>
   {
       // These are already the defaults, but being explicit helps
       o.NonExistentTimeHandling = NonExistentTimePolicy.ShiftForward;
       o.AmbiguousTimeHandling = AmbiguousTimePolicy.UseStandardTime;
   });
   ```

---

## CronExpression Returns No Occurrences

`GetNextOccurrence` returns `null` or `GetOccurrences` yields no results.

### Checklist

1. **Is the expression valid?** Use `TryParse` to check:

   ```csharp
   if (!CronExpression.TryParse(expression, out var cron))
       // Invalid expression
   ```

2. **Is the `from` time before the expected occurrence?** `GetNextOccurrence` finds the next match **after** `from`. If `from` is already past the expected time, it will find the next cycle.

3. **Does the expression match any dates in the search window?** The scheduler searches up to 4 years ahead. An expression like `0 0 31 2 *` (midnight on February 31) will never match.

4. **Is a DST gap swallowing the occurrence?** If the scheduled time falls in a DST gap, that occurrence is skipped. The scheduler advances to the next valid minute:

   ```csharp
   // "Every day at 2:30 AM" in Europe/Rome
   // On March 29, 2026 (spring forward), 2:30 AM does not exist
   // The scheduler skips to the next valid occurrence
   var cron = CronExpression.Parse("30 2 * * *");
   var next = cron.GetNextOccurrence(beforeSpringForward, TimeZoneResolver.GetTimeZone("Europe/Rome"));
   ```

5. **Check semantics (Unix vs Quartz).** For expressions with both day-of-month and day-of-week specified, Unix semantics use OR logic (matches if either field matches), Quartz uses AND logic (both must match):

   ```csharp
   var unixCron = CronExpression.Parse("0 9 15 * 1", CronSemantics.Unix);    // 15th OR Monday
   var quartzCron = CronExpression.Parse("0 9 15 * 1", CronSemantics.Quartz); // 15th AND Monday
   ```

---

## TemporalContext Shows Wrong Client Timezone

The `TemporalContext.ClientTimeZone` does not match the expected user timezone.

### Checklist

1. **Is the middleware registered?** The ASP.NET Core middleware must be in the pipeline:

   ```csharp
   app.UsePragmaticTemporal();
   ```

2. **Is the client sending timezone information?** Check the detection strategies (evaluated in priority order):

   | Priority | Strategy | Where to Check |
   |----------|----------|---------------|
   | 50 | Query string | `?tz=Europe/Rome` in the URL |
   | 100 | Header | `X-Timezone: Europe/Rome` request header |
   | 200 | JWT claim | `timezone` claim in the access token |
   | 300 | Cookie | `tz` cookie |

3. **Is the timezone ID valid?** If the client sends an invalid timezone ID and `ThrowOnInvalidTimeZone` is `false` (default), the middleware silently falls back to `TemporalOptions.DefaultTimeZone`.

4. **Is the default timezone configured?** If no detection strategy matches, `TemporalContext.ClientTimeZone` uses `TemporalOptions.DefaultTimeZone`, which defaults to UTC.

---

## Diagnostics Reference

| ID | Severity | Cause | Fix | IDE quick-fix |
|----|----------|-------|-----|---------------|
| PRAG0900 | Warning | `DateTime.Now`, `.UtcNow`, `DateTimeOffset.Now`, `.UtcNow` used directly | Inject and use `IClock` | Replaces with the equivalent read from an `IClock` member already on the type (e.g. `DateTime.UtcNow` → `_clock.UtcNow.UtcDateTime`); offered only when a clock is in scope |
| PRAG0901 | Warning | `DateTime.Today` used directly | Use `IClock.Today` or `IClock.UtcToday` | Replaces with `_clock.Now.LocalDateTime.Date` (local midnight, Kind preserved) when a clock is in scope |
| PRAG0902 | Warning | `new DateTime()` without `DateTimeKind` parameter | Specify `DateTimeKind` or use `LocalDateTime` | Appends `DateTimeKind.Utc` / `Local` / `Unspecified` (date-only constructors expand to the 7-argument overload) |
| PRAG0903 | Warning | `DateTimeOffset` relational comparison (`<`, `>`) without `.UtcDateTime` | Compare `.UtcDateTime` properties or use `ZonedDateTime` | Wraps both operands with `.UtcDateTime` (only when both are `DateTimeOffset`) |
| PRAG0904 | Info | `DateTime.Now`/`.UtcNow` in test code | Use `TestClock` for deterministic tests | Same clock-member replacement as PRAG0900 when the test class exposes an `IClock` |

The quick-fixes ship in `Pragmatic.Temporal.CodeFixers`, packaged alongside the analyzers — they appear as lightbulb actions in the IDE and support Fix All. Check the **Error List** window in Visual Studio or the build output for diagnostic details and the affected source location.

---

## FAQ

### Should I use LocalDate or DateOnly?

Use `LocalDate`. It wraps `DateOnly` but integrates with the Pragmatic ecosystem: `Period` arithmetic, `DateRange` operations, `ITemporalCalculator`, and extension methods like `Next(DayOfWeek)`. Implicit conversions exist between `LocalDate` and `DateOnly` for interop.

### When should I use ZonedDateTime vs DateTimeOffset?

Use `ZonedDateTime` when the timezone is semantically meaningful (scheduling, display, conversion). Use `DateTimeOffset` for storage and transport (databases, APIs). Convert at boundaries: `ZonedDateTime.ToUtc()` for storage, `ZonedDateTime.FromUtc(dto, zone)` for display.

### How do I handle "every day at 2:30 AM" across DST?

Use `CronExpression` with a timezone:

```csharp
var cron = CronExpression.Parse("30 2 * * *");
var next = cron.GetNextOccurrence(now, TimeZoneResolver.GetTimeZone("Europe/Rome"));
// Automatically skips non-existent times during spring forward
```

### Can I use Pragmatic.Temporal without DI?

Yes. All types (`LocalDate`, `ZonedDateTime`, `Duration`, etc.) are plain value types with no DI dependency. `TemporalCalculator` can be instantiated directly. `IClock` and `TemporalContext` are the only DI-oriented APIs.

### How does Pragmatic.Temporal differ from NodaTime?

Pragmatic.Temporal uses .NET BCL timezone data (always current via OS updates), focuses on business day calculations and cron scheduling, and provides Roslyn analyzers. NodaTime ships its own TZDB data, supports multiple calendar systems, and has a larger API surface. See the comparison table in the README for details.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working temporal usage across business logic.
- **Core Types Guide**: See [core-types.md](/modules/temporal/core-types/) for all types, storage formats, and conversion rules.
- **DST Handling Guide**: See [dst-handling.md](/modules/temporal/dst-handling/) for detailed DST policy examples.
- **Testing Guide**: See [testing.md](/modules/temporal/testing/) for TestClock, TestTemporalContext, and test patterns.
