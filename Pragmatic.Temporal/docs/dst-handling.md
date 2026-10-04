# DST Handling

How Pragmatic.Temporal handles Daylight Saving Time transitions: explicitly, with policies you choose instead of silent surprises.

## The DST Problem

Twice a year, wall clocks in DST-observing zones misbehave:

1. **Spring forward: non-existent times.** In Europe/Rome on March 31, 2024, clocks jump from 02:00 directly to 03:00. The local time **02:30 does not exist** that day.
2. **Fall back: ambiguous times.** In Europe/Rome on October 27, 2024, clocks fall back from 03:00 to 02:00. The local time **02:30 occurs twice**: once at offset +02:00 (daylight) and once at +01:00 (standard).

Any code that converts a wall-clock time to an instant must decide what to do in these two cases. Pragmatic.Temporal forces that decision into two explicit policy enums instead of letting the runtime pick silently.

## The Two Policies

**When you need them**: every time a local (wall-clock) time is converted to a UTC instant, such as creating a `ZonedDateTime` from a `DateTime`/`LocalDateTime`, or converting user input to UTC through `TemporalContext`.

```csharp
using Pragmatic.Temporal.Types;

public enum NonExistentTimePolicy   // spring-forward gap
{
    ShiftForward,     // default: 02:30 → 03:00 (first valid time after the gap)
    ThrowException    // throws NonExistentTimeException
}

public enum AmbiguousTimePolicy     // fall-back overlap
{
    UseStandardTime,  // default: 02:30 → 02:30 +01:00 (the later occurrence)
    UseDaylightTime,  //          02:30 → 02:30 +02:00 (the earlier occurrence)
    ThrowException    // throws AmbiguousTimeException
}
```

The defaults (`ShiftForward`, `UseStandardTime`) are deterministic and never throw, which is good for background processing. Choose `ThrowException` at input boundaries when the user should resolve the ambiguity.

## Creating a ZonedDateTime from a Local Time

**When you need it**: you have a wall-clock time (user input, an appointment time) and a timezone, and you need the actual instant.

**What you write**:

```csharp
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");

// 02:30 on March 31, 2024 does not exist in Rome (spring forward)
var local = new DateTime(2024, 3, 31, 2, 30, 0);

// Default policies: gap → shift forward, ambiguity → standard time
var shifted = ZonedDateTime.FromLocal(local, rome);
// → 2024-03-31T03:00:00+02:00[Europe/Rome]

// Explicit policies
var explicitPolicy = ZonedDateTime.FromLocal(local, rome,
    NonExistentTimePolicy.ShiftForward,
    AmbiguousTimePolicy.UseDaylightTime);

// Strict: throws on BOTH gap and ambiguity, for input validation
var strict = ZonedDateTime.FromLocalStrict(local, rome);
// → throws NonExistentTimeException
```

**What you get**: a `ZonedDateTime` whose UTC instant is well-defined even in the two DST edge windows, or a specific exception if you asked for one. Note that `FromLocal` takes a `TimeZoneInfo`, not a timezone string; resolve IANA IDs with `TimeZoneResolver.GetTimeZone("Europe/Rome")` first.

The same conversion is available from `LocalDateTime`, including a string-zone convenience overload:

```csharp
var appointment = new LocalDateTime(2024, 10, 27, 2, 30);

var zoned = appointment.InZone("Europe/Rome",
    ambiguousPolicy: AmbiguousTimePolicy.ThrowException);
// → throws AmbiguousTimeException (02:30 occurs twice that night)
```

If you already have a UTC instant, `ZonedDateTime.FromUtc(utc, zone)` is always safe: UTC has no gaps and no ambiguity. `ZonedDateTime.Now(zone, clock)` builds on it.

## Handling the Exceptions

**When you need it**: input validation with `ThrowException` policies, when the user must disambiguate.

```csharp
try
{
    var zoned = ZonedDateTime.FromLocalStrict(userInput, rome);
}
catch (NonExistentTimeException ex)
{
    // ex.LocalTime: the DateTime that doesn't exist
    // ex.TimeZone: the zone where it doesn't exist
    // "That time is skipped by DST: pick a time from 03:00 onwards."
}
catch (AmbiguousTimeException ex)
{
    // ex.LocalTime, ex.TimeZone
    // "That time occurs twice: did you mean the earlier or the later one?"
}
```

Both exceptions derive from `InvalidOperationException` and carry the offending `LocalTime` and `TimeZone`, so error messages can guide the user precisely.

## Request-Scoped Conversions with TemporalContext

**When you need it**: converting client or business wall-clock input to UTC inside request handling, without threading zone and policy parameters through every call.

**What you write**:

```csharp
public sealed class BookingService(TemporalContext temporal)
{
    public DateTimeOffset ToStorageUtc(LocalDateTime clientWallClock)
        => temporal.ClientToUtc(clientWallClock);            // context's default policies

    public DateTimeOffset ToStorageUtcStrict(DateTime clientWallClock)
        => temporal.ClientToUtc(clientWallClock,
            NonExistentTimePolicy.ThrowException,
            AmbiguousTimePolicy.ThrowException);             // per-call override
}
```

**What you get**: `ClientToUtc`/`BusinessToUtc` accept both `DateTime` and `LocalDateTime`, use the context's `ClientTimeZone`/`BusinessTimeZone`, and apply the context's `NonExistentTimeHandling`/`AmbiguousTimeHandling` defaults, with per-call policy overloads when one boundary needs stricter rules.

## DST-Safe Arithmetic: Wall Clock vs Physical Time

**When you need it**: "same time tomorrow" (a daily stand-up) is *not* the same operation as "24 hours from now" (a cache expiry). Around DST transitions they differ by an hour.

**What you write**:

```csharp
var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
// Saturday March 30, 2024, 12:00 in Rome: the night before spring forward
var saturdayNoon = ZonedDateTime.FromLocal(new DateTime(2024, 3, 30, 12, 0, 0), rome);

// CALENDAR arithmetic: keeps the wall-clock time, re-resolves the offset
var sundayNoon = saturdayNoon.AddDays(1);
// → 2024-03-31T12:00:00+02:00, still noon, but only 23 physical hours later

// PHYSICAL arithmetic: adds exact elapsed time on the UTC instant
var plus24h = saturdayNoon.Add(Duration.FromHours(24));
// → 2024-03-31T13:00:00+02:00, a full 24h later: wall clock moved to 13:00
```

**What you get**:

| Operation | Semantics | DST behavior |
|-----------|-----------|--------------|
| `AddDays` / `AddMonths` / `AddYears` / `AddHours` / `AddMinutes` | Wall clock ("same local time") | Local time preserved; elapsed time may be 23h or 25h. If the target local time lands in a gap/overlap, it is re-resolved with the default policies (`ShiftForward`, `UseStandardTime`). |
| `Add(Duration)` / `Add(TimeSpan)` / `+`/`-` operators | Physical elapsed time on the UTC instant | Exact duration; the local time-of-day may shift across a transition. |

This is the core rule of the library: **`Duration` ≠ calendar arithmetic**. Pick the operation that matches the domain meaning, not the one that is closer at hand.

## Scheduling Across DST (Cron)

**When you need it**: recurring jobs defined on wall-clock time ("every day at 02:30") in a DST-observing zone.

**What you write**:

```csharp
public sealed class MaintenanceScheduler(IClock clock)
{
    private static readonly CronExpression NightlyRun = CronExpression.Parse("30 2 * * *"); // every day at 02:30

    public DateTimeOffset? NextRun()
    {
        var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
        return NightlyRun.GetNextOccurrence(clock.UtcNow, rome);
    }
}
```

**What you get**: occurrences are evaluated on the wall clock of the given zone (UTC when omitted), with the two edge cases handled for you:

- **Spring forward**: an occurrence falling inside the gap (02:30 doesn't exist on March 31) is skipped forward to the next valid minute, so the job is not lost.
- **Fall back**: for an ambiguous time the scheduler picks the offset producing the **earliest instant strictly after `from`**, so the sequence keeps advancing and never stalls or double-fires on the repeated hour.

`GetOccurrences(from, until, zone)` streams the same DST-aware sequence lazily.

## Pitfall: `LocalDateTime.ToDateTimeOffset(zone)`

`ToDateTimeOffset(zone)` resolves the offset with a plain `TimeZoneInfo.GetUtcOffset`: it does **not** apply the DST policies. Inside a gap or overlap it silently produces whatever offset the runtime returns, without shifting, choosing, or throwing.

```csharp
var dt = new LocalDateTime(2024, 3, 31, 2, 30);

var risky = dt.ToDateTimeOffset(rome);   // ⚠️ no gap/ambiguity handling
var safe  = dt.InZone(rome);             // ✅ policy-aware ZonedDateTime
```

Prefer `InZone(...)` (or `ZonedDateTime.FromLocal`) whenever the value could fall in a transition window; reserve `ToDateTimeOffset(zone)` for times you know are safe (e.g., values already validated, or zones without DST).

## Best Practices

### 1. Store UTC, convert at boundaries

```csharp
public class Event
{
    public DateTimeOffset StartsAtUtc { get; set; }          // storage: always UTC

    public ZonedDateTime StartsAtLocal(string timezoneId)    // display: convert on the way out
        => ZonedDateTime.FromUtc(StartsAtUtc, timezoneId);
}
```

### 2. Strict at input, forgiving in background work

User-facing input should surface DST problems (`ThrowException` + a helpful message); unattended processing should use the never-throwing defaults (`ShiftForward`, `UseStandardTime`) so a twice-a-year edge case can't crash a job.

### 3. Test the transition dates

`TestClock` (from `Pragmatic.Temporal.Testing`) ships helpers that park the clock one second before a transition: `SetBeforeRomeSpringForward()`, `SetBeforeRomeFallBack()`, `SetBeforeUsEasternSpringForward()`. See [Testing](testing.md).

## Common Timezone DST Rules

| Region | Spring forward | Fall back |
|--------|---------------|-----------|
| US Eastern | 2nd Sunday March, 2 AM | 1st Sunday November, 2 AM |
| US Pacific | 2nd Sunday March, 2 AM | 1st Sunday November, 2 AM |
| Central Europe | Last Sunday March, 2 AM | Last Sunday October, 3 AM |
| UK | Last Sunday March, 1 AM | Last Sunday October, 2 AM |
| Australia (Sydney) | 1st Sunday October, 2 AM | 1st Sunday April, 3 AM |

Some regions don't observe DST at all (Arizona, most of Asia and Africa), but don't hardcode that: resolve zones through `TimeZoneResolver` and let the policies handle whatever rules apply.
